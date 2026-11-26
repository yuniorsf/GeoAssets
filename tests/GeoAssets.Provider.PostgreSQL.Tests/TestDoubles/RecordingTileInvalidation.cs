using System.Collections.Concurrent;
using GeoAssets.Core.Rendering;

namespace GeoAssets.Provider.PostgreSQL.Tests.TestDoubles;

/// <summary>
/// An <see cref="ITileInvalidation"/> recording each call's regions, synchronously on the call (so a fire-and-forget
/// caller's call is recorded by the time the write returns). <paramref name="onInvalidate"/> runs on each call too — e.g.
/// to look at the database as the invalidation sees it — and <paramref name="fail"/> makes every call throw after
/// recording it.
/// </summary>
internal sealed class RecordingTileInvalidation(Action<IReadOnlyCollection<DirtyTileRegion>>? onInvalidate = null, bool fail = false)
    : ITileInvalidation
{
    private readonly ConcurrentQueue<IReadOnlyList<DirtyTileRegion>> _calls = new();

    public IReadOnlyList<IReadOnlyList<DirtyTileRegion>> Calls => [.. _calls];

    public Task InvalidateAsync(IReadOnlyCollection<DirtyTileRegion> regions, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(regions);
        _calls.Enqueue([.. regions]);
        onInvalidate?.Invoke(regions);
        if (fail) throw new InvalidOperationException("The tile cache is down.");
        return Task.CompletedTask;
    }
}
