using GeoAssets.Core.Rendering;

namespace GeoAssets.Server.Tiles;

/// <summary>
/// The kernel's <see cref="ITileInvalidation"/> (ADR-002 D6, XD01-434), which the PostGIS writers call after each
/// committed write: it first advances <see cref="TileVersions"/> (so every <c>{v}</c> <c>style.json</c> publishes from now
/// on is new), then evicts the write's dirty tiles from the <see cref="ITileCache"/> — each region's
/// <see cref="DirtyTileRegion.TileRanges"/> grown by <see cref="DirtyTileBuffer"/>, at the zooms the cache holds (up to
/// <see cref="TileServingOptions.MaxCachedZoom"/>). A write whose ranges hold more than
/// <see cref="TileServingOptions.MaxEvictedTilesPerWrite"/> tiles is not evicted tile by tile: <see cref="TileVersions.Flush"/>
/// changes every cache version instead, so nothing cached before it is served again (the old entries age out of the cache).
///
/// <para><b>Order.</b> Advancing before evicting is what keeps a tile drawn from pre-commit rows out of the cache: the tile
/// endpoint re-checks the generation after storing a tile it drew and drops it when the generation moved (see
/// <c>TileEndpointExtensions</c>), so either it sees this write's advance or its store happened before this eviction.</para>
///
/// <para>Idempotent, thread-safe, and it does no I/O; it does not throw for valid regions (a writer would only log it).</para>
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

    public TileInvalidation(ITileCache cache, TileVersions versions, TileServingOptions options)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(versions);
        ArgumentNullException.ThrowIfNull(options);

        _cache = cache;
        _versions = versions;
        _options = options;
    }

    /// <inheritdoc />
    public async Task InvalidateAsync(IReadOnlyCollection<DirtyTileRegion> regions, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(regions);
        if (regions.Count == 0) return;

        var ranges = new List<TileRange>();
        long tiles = 0;
        foreach (var region in regions)
        {
            if (region.MinZoom > _options.MaxCachedZoom) continue;

            foreach (var range in region.TileRanges(DirtyTileBuffer).TakeWhile(r => r.Zoom <= _options.MaxCachedZoom))
            {
                tiles += range.Count;
                if (tiles > _options.MaxEvictedTilesPerWrite)
                {
                    _versions.Flush();
                    _versions.Advance();
                    TileMetrics.Invalidated(TileMetrics.Flushed);
                    return;
                }

                ranges.Add(range);
            }
        }

        _versions.Advance();
        foreach (var range in ranges)
            await _cache.RemoveAsync(range, ct).ConfigureAwait(false);

        TileMetrics.Invalidated(TileMetrics.Evicted);
    }
}
