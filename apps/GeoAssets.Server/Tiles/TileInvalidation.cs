using GeoAssets.Core.Rendering;
using GeoAssets.Identity.Authorization.Repositories;

namespace GeoAssets.Server.Tiles;

/// <summary>
/// The kernel's <see cref="ITileInvalidation"/> (ADR-002 D6, XD01-434, XD01-458), which the PostGIS writers call after each
/// committed write: it advances <see cref="TileVersions"/> for the scopes that can see the write (so the <c>{v}</c>
/// <c>style.json</c> publishes to them from now on is new, and only to them), and evicts the write's dirty tiles from the
/// <see cref="ITileCache"/> — each region's <see cref="DirtyTileRegion.TileRanges"/> grown by <see cref="DirtyTileBuffer"/>,
/// at the zooms the cache holds (up to <see cref="TileServingOptions.MaxCachedZoom"/>). A write whose ranges hold more than
/// <see cref="TileServingOptions.MaxEvictedTilesPerWrite"/> tiles is not evicted tile by tile: <see cref="TileVersions.Flush"/>
/// changes every cache version instead, so nothing cached before it is served again (the old entries age out of the cache).
///
/// <para><b>Scopes.</b> A write of an organization's features advances that organization's generation and those of the
/// organizations holding an active, unexpired grant on it (<see cref="TileScopeResolver.GranteesAsync"/>); a write of the
/// organization-less (shared) features — or one whose writer names no organization, or whose grantees can't be read —
/// advances the shared generation, which every scope's includes.</para>
///
/// <para><b>Order.</b> In process, advancing the writer's organization before evicting is what keeps a tile drawn from
/// pre-commit rows out of the cache: the tile endpoint re-checks <see cref="TileVersions.Generation"/> after storing a tile
/// it drew and drops it when it moved (see <c>TileEndpointExtensions</c>), so either it sees this write's advance or its
/// store happened before this eviction. Shared (<see cref="TileVersionSync"/>), it moves <see cref="TileVersions.Generation"/>
/// and evicts first, then publishes the new generations the store answers — so a <c>{v}</c> is only ever a stored one.</para>
///
/// <para>Idempotent and thread-safe; it does not throw for valid regions (a writer would only log it), unless the shared
/// store refuses the change — then the tiles were evicted here and <see cref="TileVersionSync"/> flushes every replica once
/// the store is back.</para>
/// </summary>
public sealed class TileInvalidation : ITileInvalidation
{
    /// <summary>
    /// How far past a change, in tiles, a tile may draw it: a raster tile's 8 px symbol buffer of its 256 px
    /// (<c>SkiaRasterTileSource</c>), which also covers a vector tile's 64 of 4,096 units.
    /// </summary>
    public const double DirtyTileBuffer = 8.0 / 256;

    private readonly ITileCache _cache;
    private readonly TileVersions _versions;
    private readonly TileServingOptions _options;
    private readonly IServiceScopeFactory? _scopes;
    private readonly TimeProvider _timeProvider;
    private readonly TileVersionSync? _sync;

    /// <summary>An invalidation in process that reads no grants: every write advances its writer's organization only.</summary>
    public TileInvalidation(ITileCache cache, TileVersions versions, TileServingOptions options)
        : this(cache, versions, options, scopes: null, TimeProvider.System, sync: null)
    {
    }

    /// <summary>
    /// An invalidation that reads the writer's organization's grantees from the <see cref="IOrganizationGrantRepository"/>
    /// of a scope of <paramref name="scopes"/> (<c>null</c> or none registered = no grantees) and, with
    /// <paramref name="sync"/>, shares its versions with every replica.
    /// </summary>
    public TileInvalidation(
        ITileCache cache, TileVersions versions, TileServingOptions options, IServiceScopeFactory? scopes, TimeProvider timeProvider, TileVersionSync? sync)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(versions);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _cache = cache;
        _versions = versions;
        _options = options;
        _scopes = scopes;
        _timeProvider = timeProvider;
        _sync = sync;
    }

    /// <inheritdoc />
    public Task InvalidateAsync(IReadOnlyCollection<DirtyTileRegion> regions, CancellationToken ct) =>
        InvalidateAsync(regions, Guid.Empty, ct);

    /// <inheritdoc />
    public async Task InvalidateAsync(IReadOnlyCollection<DirtyTileRegion> regions, Guid organizationId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(regions);
        if (regions.Count == 0) return;

        var evictable = TileEviction.TryDirtyRanges(regions, _options, out var ranges);
        if (_sync is not null)
        {
            await InvalidateSharedAsync(_sync, regions, organizationId, evictable, ranges, ct).ConfigureAwait(false);
            return;
        }

        if (!evictable)
        {
            _versions.Flush();
            _versions.Advance([organizationId]);
            TileMetrics.Invalidated(TileMetrics.Flushed);
        }
        else
        {
            _versions.Advance([organizationId]);
            await TileEviction.EvictAsync(_cache, ranges, ct).ConfigureAwait(false);
            TileMetrics.Invalidated(TileMetrics.Evicted);
        }

        if (organizationId == Guid.Empty) return;

        var grantees = await GranteesAsync(organizationId, ct).ConfigureAwait(false);
        if (grantees is null) _versions.Advance();
        else if (grantees.Count > 0) _versions.Advance(grantees);
    }

    private async Task InvalidateSharedAsync(
        TileVersionSync sync, IReadOnlyCollection<DirtyTileRegion> regions, Guid organizationId, bool evictable, List<TileRange> ranges, CancellationToken ct)
    {
        _versions.Invalidated();
        if (evictable) await TileEviction.EvictAsync(_cache, ranges, ct).ConfigureAwait(false);

        IReadOnlyCollection<Guid> scopes = [Guid.Empty];
        if (organizationId != Guid.Empty && await GranteesAsync(organizationId, ct).ConfigureAwait(false) is { } grantees)
            scopes = [organizationId, .. grantees];

        await sync.PublishAsync(scopes, flush: !evictable, evictable ? regions : [], ct).ConfigureAwait(false);
        TileMetrics.Invalidated(evictable ? TileMetrics.Evicted : TileMetrics.Flushed);
    }

    /// <summary>The organizations other than <paramref name="organizationId"/> that see its features through a grant; <c>null</c> when they can't be read.</summary>
    private async Task<IReadOnlyCollection<Guid>?> GranteesAsync(Guid organizationId, CancellationToken ct)
    {
        if (_scopes is null) return [];

        try
        {
            var scope = _scopes.CreateAsyncScope();
            await using (scope.ConfigureAwait(false))
            {
                return await TileScopeResolver.GranteesAsync(
                    organizationId, scope.ServiceProvider.GetService<IOrganizationGrantRepository>(), _timeProvider.GetUtcNow().UtcDateTime, ct)
                    .ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return null;
        }
    }
}
