using GeoAssets.Core.Rendering;

namespace GeoAssets.Server.Tiles;

/// <summary>
/// Which cached tiles a write's dirty regions reach, and their eviction (XD01-434, shared by the local writes'
/// <see cref="TileInvalidation"/> and the peers' changes of <see cref="TileVersionSync"/>, XD01-458): each region's
/// <see cref="DirtyTileRegion.TileRanges"/> grown by <see cref="TileInvalidation.DirtyTileBuffer"/>, at the zooms the cache
/// holds (up to <see cref="TileServingOptions.MaxCachedZoom"/>), unless they hold more than
/// <see cref="TileServingOptions.MaxEvictedTilesPerWrite"/> tiles.
/// </summary>
internal static class TileEviction
{
    /// <summary>The cached ranges <paramref name="regions"/> dirty; <c>false</c> when they are too many tiles to evict one by one.</summary>
    public static bool TryDirtyRanges(IEnumerable<DirtyTileRegion> regions, TileServingOptions options, out List<TileRange> ranges)
    {
        ranges = [];
        long tiles = 0;
        foreach (var region in regions)
        {
            if (region.MinZoom > options.MaxCachedZoom) continue;

            foreach (var range in region.TileRanges(TileInvalidation.DirtyTileBuffer).TakeWhile(r => r.Zoom <= options.MaxCachedZoom))
            {
                tiles += range.Count;
                if (tiles > options.MaxEvictedTilesPerWrite)
                {
                    ranges = [];
                    return false;
                }

                ranges.Add(range);
            }
        }

        return true;
    }

    public static async Task EvictAsync(ITileCache cache, IEnumerable<TileRange> ranges, CancellationToken ct)
    {
        foreach (var range in ranges)
            await cache.RemoveAsync(range, ct).ConfigureAwait(false);
    }

    /// <summary>Evicts every cached tile: the whole pyramid at each cached zoom (what a cache walks is bounded by what it holds).</summary>
    public static Task ResetAsync(ITileCache cache, TileServingOptions options, CancellationToken ct) =>
        EvictAsync(cache, Enumerable.Range(TileAddress.MinZoom, options.MaxCachedZoom - TileAddress.MinZoom + 1).Select(World), ct);

    private static TileRange World(int zoom)
    {
        var last = (1 << zoom) - 1;
        return new TileRange(zoom, 0, 0, last, last);
    }
}
