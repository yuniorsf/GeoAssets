namespace GeoAssets.Core.Rendering;

/// <summary>
/// Read port of the LOD calibration (ADR-002 D6/D7, XD01-439): how dense one asset type's features are per tile, measured
/// where they are stored (the PostGIS implementation reads <c>geom_3857</c>). A shared contract in GeoAssets.Core, so a
/// plugin's sampler and the host's <see cref="LodCalibrator"/> meet in the default load context. A sampler answers in the
/// caller's ambient tenant scope, exactly as the tile sources do (the caller's organization's features plus the shared
/// ones), and every read it makes is bounded by its arguments — never a scan of a whole catalog in one call.
/// </summary>
public interface ITileDensitySampler
{
    /// <summary>
    /// At most <paramref name="maxSeeds"/> centroids of <paramref name="assetTypeId"/>'s features, drawn as a sample
    /// (not merely the first rows of the catalog): the tiles they fall in are the tiles the calibration measures. Empty
    /// when the type has no feature in scope.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="assetTypeId"/> is <c>null</c> or blank.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxSeeds"/> is not positive.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled before the read.</exception>
    /// <exception cref="TimeoutException">The read ran past the sampler's statement timeout.</exception>
    Task<IReadOnlyList<TileDensitySeed>> SampleSeedsAsync(string assetTypeId, int maxSeeds, CancellationToken ct);

    /// <summary>
    /// For each of <paramref name="tiles"/> (one zoom), how many of <paramref name="assetTypeId"/>'s features its
    /// buffered envelope intersects and their vertices, reading at most <paramref name="maxFeaturesPerTile"/> features
    /// per tile (a tile that reaches it is <see cref="TileDensitySample.Saturated"/>). One sample per tile, in the order
    /// given.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="assetTypeId"/> is <c>null</c> or blank, or the tiles are not all of one zoom.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxFeaturesPerTile"/> is not positive.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled before the read.</exception>
    /// <exception cref="TimeoutException">The read ran past the sampler's statement timeout.</exception>
    Task<IReadOnlyList<TileDensitySample>> MeasureAsync(
        string assetTypeId, IReadOnlyList<TileAddress> tiles, int maxFeaturesPerTile, CancellationToken ct);
}
