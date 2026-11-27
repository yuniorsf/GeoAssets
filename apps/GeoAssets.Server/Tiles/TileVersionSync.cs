using GeoAssets.Core.Rendering;

namespace GeoAssets.Server.Tiles;

/// <summary>
/// The tile versions every kernel replica over one database shares (ADR-002 D6, XD01-458), through an
/// <see cref="ITileVersionStore"/>: this process's <see cref="TileVersions"/> only ever publishes values the store holds.
/// <list type="bullet">
///   <item><b>Startup</b> (<see cref="EnsureReadyAsync"/>, before the first <c>{v}</c> is published): reads the shared epoch,
///   flush count and shared generation, then flushes once — every replica's cache versions change, as a restart's random
///   epoch did — so a write whose invalidation a crashed process never published can't stay cached anywhere.</item>
///   <item><b>Per organization</b> (<see cref="EnsureCurrentAsync"/>): its generation is read once, on its first request.</item>
///   <item><b>Own writes</b> (<see cref="PublishAsync"/>): the store advances the scopes and announces the change; this
///   process adopts the generations it answers. A change the store refuses leaves <see cref="RecoveryPending"/>: the
///   listener flushes every replica once the store answers again (<see cref="RecoverAsync"/>).</item>
///   <item><b>Peers' writes</b> (<see cref="ApplyAsync"/>): moves <see cref="TileVersions.Generation"/>, evicts the change's
///   dirty tiles, then adopts its generations and flush — evicting first, so the new <c>{v}</c> never serves a stale tile.
///   A change announced incomplete is a gap: the cache is emptied and the versions re-read.</item>
///   <item><b>Every (re)subscription</b> (<see cref="OnSubscribedAsync"/>): the changes announced while unsubscribed are
///   lost, so it treats them as a gap too.</item>
/// </list>
/// Every value adopted is the larger of the one held and the one read, so no scope's <c>{v}</c> goes backwards. Singleton.
/// </summary>
public sealed partial class TileVersionSync
{
    private readonly ITileVersionStore _store;
    private readonly TileVersions _versions;
    private readonly ITileCache _cache;
    private readonly TileServingOptions _options;
    private readonly ILogger<TileVersionSync> _logger;
    private readonly Lock _gate = new();
    private Task? _ready;
    private int _recoveryPending;

    public TileVersionSync(ITileVersionStore store, TileVersions versions, ITileCache cache, TileServingOptions options, ILogger<TileVersionSync> logger)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(versions);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _store = store;
        _versions = versions;
        _cache = cache;
        _options = options;
        _logger = logger;
    }

    /// <summary>This process's id in the changes it announces (its own come back to it and are skipped).</summary>
    public string Origin { get; } = Guid.NewGuid().ToString("N");

    /// <summary>Whether a change of this process's was refused by the store and every replica has yet to be flushed for it.</summary>
    public bool RecoveryPending => Volatile.Read(ref _recoveryPending) != 0;

    /// <summary>Reads the shared versions and flushes once, on the first call (a failed attempt is tried again by the next).</summary>
    public Task EnsureReadyAsync(CancellationToken ct)
    {
        Task ready;
        lock (_gate)
        {
            if (_ready is null || _ready.IsFaulted || _ready.IsCanceled) _ready = StartAsync();
            ready = _ready;
        }

        return ready.IsCompletedSuccessfully ? Task.CompletedTask : ready.WaitAsync(ct);
    }

    /// <summary>Makes the versions of <paramref name="organizationId"/>'s scope current: ready, and its generation read once.</summary>
    public async Task EnsureCurrentAsync(Guid organizationId, CancellationToken ct)
    {
        await EnsureReadyAsync(ct).ConfigureAwait(false);
        if (organizationId == Guid.Empty || _versions.Knows(organizationId)) return;

        var snapshot = await _store.LoadAsync([organizationId], ct).ConfigureAwait(false);
        _versions.Adopt(snapshot.Generations, snapshot.Flushes);
    }

    /// <summary>
    /// Advances <paramref name="scopes"/> (and, when <paramref name="flush"/>, the flush count) in the store, announcing
    /// <paramref name="regions"/>, and adopts what it answers. Refused: <see cref="RecoveryPending"/> is set (and, for a
    /// flush, this process's cache emptied, since nothing else will drop its stale tiles) and the failure rethrown.
    /// </summary>
    public async Task PublishAsync(IReadOnlyCollection<Guid> scopes, bool flush, IReadOnlyCollection<DirtyTileRegion> regions, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        ArgumentNullException.ThrowIfNull(regions);

        try
        {
            await EnsureReadyAsync(ct).ConfigureAwait(false);
            var change = await _store.AdvanceAsync(Origin, scopes, flush, regions, ct).ConfigureAwait(false);
            _versions.Adopt(change.Generations, change.Flushes);
        }
        catch (Exception ex)
        {
            Volatile.Write(ref _recoveryPending, 1);
            LogPublishFailed(_logger, ex, scopes.Count);
            if (flush) await TileEviction.ResetAsync(_cache, _options, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>A peer's change: its dirty tiles evicted, then its versions adopted; this process's own are skipped.</summary>
    public async Task ApplyAsync(TileVersionChange change, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(change);
        if (string.Equals(change.Origin, Origin, StringComparison.Ordinal)) return;

        _versions.Invalidated();
        if (!change.IsComplete)
        {
            await ResynchronizeAsync(ct).ConfigureAwait(false);
            TileMetrics.PeerChanged(TileMetrics.Resynchronized);
            return;
        }

        if (TileEviction.TryDirtyRanges(change.Regions, _options, out var ranges))
            await TileEviction.EvictAsync(_cache, ranges, ct).ConfigureAwait(false);
        else
            await TileEviction.ResetAsync(_cache, _options, ct).ConfigureAwait(false);

        _versions.Adopt(change.Generations, change.Flushes);
        TileMetrics.PeerChanged(change.Flushes is null ? TileMetrics.Evicted : TileMetrics.Flushed);
    }

    /// <summary>Subscribed to the store's changes (again): ready, then every tile treated as dirty and every known version re-read.</summary>
    public async Task OnSubscribedAsync(CancellationToken ct)
    {
        await EnsureReadyAsync(ct).ConfigureAwait(false);
        _versions.Invalidated();
        await ResynchronizeAsync(ct).ConfigureAwait(false);
        LogSubscribed(_logger, _versions.KnownScopes.Count);
    }

    /// <summary>When <see cref="RecoveryPending"/>: flushes every replica through the store. Throws while the store refuses it.</summary>
    public async Task RecoverAsync(CancellationToken ct)
    {
        if (!RecoveryPending) return;

        await EnsureReadyAsync(ct).ConfigureAwait(false);
        Volatile.Write(ref _recoveryPending, 0);
        try
        {
            var change = await _store.AdvanceAsync(Origin, [], flush: true, [], ct).ConfigureAwait(false);
            _versions.Adopt(change.Generations, change.Flushes);
            LogRecovered(_logger, change.Flushes ?? _versions.Flushes);
        }
        catch
        {
            Volatile.Write(ref _recoveryPending, 1);
            throw;
        }
    }

    private async Task StartAsync()
    {
        var snapshot = await _store.LoadAsync([Guid.Empty], CancellationToken.None).ConfigureAwait(false);
        var flushed = await _store.AdvanceAsync(Origin, [], flush: true, [], CancellationToken.None).ConfigureAwait(false);
        _versions.Adopt(snapshot);
        _versions.Adopt(flushed.Generations, flushed.Flushes);
        LogReady(_logger, snapshot.Epoch, _versions.Flushes);
    }

    private async Task ResynchronizeAsync(CancellationToken ct)
    {
        await TileEviction.ResetAsync(_cache, _options, ct).ConfigureAwait(false);
        var snapshot = await _store.LoadAsync([Guid.Empty, .. _versions.KnownScopes], ct).ConfigureAwait(false);
        _versions.Adopt(snapshot);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Shared tile versions ready: epoch {Epoch}, flush {Flushes} (this replica's start flushed every replica's tile cache versions)")]
    private static partial void LogReady(ILogger logger, string epoch, long flushes);

    [LoggerMessage(Level = LogLevel.Information, Message = "Subscribed to the shared tile versions; the tile cache was emptied and {ScopeCount} scopes' generations re-read")]
    private static partial void LogSubscribed(ILogger logger, int scopeCount);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The shared tile version store refused a change of {ScopeCount} scopes; this replica evicted its tiles, and every replica is flushed once the store answers again")]
    private static partial void LogPublishFailed(ILogger logger, Exception ex, int scopeCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Flushed every replica's tile cache versions (flush {Flushes}) for a change the shared tile version store had refused")]
    private static partial void LogRecovered(ILogger logger, long flushes);
}
