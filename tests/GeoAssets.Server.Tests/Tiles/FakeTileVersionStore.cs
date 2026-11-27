using System.Collections.Concurrent;
using System.Threading.Channels;
using GeoAssets.Core.Rendering;

namespace GeoAssets.Server.Tests.Tiles;

/// <summary>
/// An in-memory <see cref="ITileVersionStore"/> (XD01-458): the shared epoch, flush count and generations, the calls made,
/// a switch refusing every change (<see cref="Refuse"/>), and a scripted subscription — each <see cref="ListenAsync"/>
/// waits for <see cref="Deliver"/>ed changes, or ends with an <see cref="Drop"/>ped exception (a lost subscription).
/// </summary>
internal sealed class FakeTileVersionStore : ITileVersionStore
{
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, long> _generations = [];
    private readonly Channel<object> _events = Channel.CreateUnbounded<object>();
    private readonly Channel<bool> _subscribed = Channel.CreateUnbounded<bool>();
    private readonly Channel<bool> _applied = Channel.CreateUnbounded<bool>();
    private int _subscriptions;

    public string Epoch { get; set; } = "feed0458";

    public long Flushes { get; private set; }

    public bool Refuse { get; set; }

    /// <summary>Subscriptions refused before one succeeds (each throws, as a database that is down would).</summary>
    public int RefuseSubscriptions { get; set; }

    public ConcurrentQueue<IReadOnlyCollection<Guid>> Loads { get; } = new();

    public ConcurrentQueue<(IReadOnlyCollection<Guid> Scopes, bool Flush, IReadOnlyCollection<DirtyTileRegion> Regions)> Advances { get; } = new();

    public int Subscriptions => Volatile.Read(ref _subscriptions);

    public long GenerationOf(Guid scope)
    {
        lock (_gate) return _generations.GetValueOrDefault(scope);
    }

    public void Set(Guid scope, long generation)
    {
        lock (_gate) _generations[scope] = generation;
    }

    public void Deliver(TileVersionChange change) => _events.Writer.TryWrite(change);

    public void Drop() => _events.Writer.TryWrite(new InvalidOperationException("The connection was lost."));

    public Task<bool> WaitSubscribedAsync() => SignalledAsync(_subscribed);

    public Task<bool> WaitAppliedAsync() => SignalledAsync(_applied);

    public Task<TileVersionSnapshot> LoadAsync(IReadOnlyCollection<Guid> scopes, CancellationToken ct)
    {
        Loads.Enqueue([.. scopes]);
        lock (_gate)
        {
            return Task.FromResult(new TileVersionSnapshot(Epoch, Flushes, scopes.Distinct().ToDictionary(s => s, s => _generations.GetValueOrDefault(s))));
        }
    }

    public Task<TileVersionChange> AdvanceAsync(string origin, IReadOnlyCollection<Guid> scopes, bool flush, IReadOnlyCollection<DirtyTileRegion> regions, CancellationToken ct)
    {
        if (Refuse) throw new InvalidOperationException("The database is down.");

        Advances.Enqueue(([.. scopes], flush, [.. regions]));
        lock (_gate)
        {
            var generations = new Dictionary<Guid, long>();
            foreach (var scope in scopes.Distinct())
                generations[scope] = _generations[scope] = _generations.GetValueOrDefault(scope) + 1;

            if (flush) Flushes++;
            return Task.FromResult(new TileVersionChange(origin, generations, flush ? Flushes : null, [.. regions]));
        }
    }

    public async Task ListenAsync(Func<CancellationToken, Task> subscribed, Func<TileVersionChange, CancellationToken, Task> changed, CancellationToken ct)
    {
        Interlocked.Increment(ref _subscriptions);
        if (RefuseSubscriptions > 0)
        {
            RefuseSubscriptions--;
            throw new InvalidOperationException("The database is down.");
        }

        await subscribed(ct);
        _subscribed.Writer.TryWrite(true);
        await foreach (var item in _events.Reader.ReadAllAsync(ct))
        {
            if (item is Exception lost) throw lost;

            await changed((TileVersionChange)item, ct);
            _applied.Writer.TryWrite(true);
        }
    }

    private static async Task<bool> SignalledAsync(Channel<bool> signal)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            return await signal.Reader.ReadAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
