using System.Globalization;

namespace GeoAssets.Core.Rendering;

/// <summary>
/// How <see cref="LodCalibrator"/> samples and what it recommends against (ADR-002 D6/D7, XD01-439). The budgets are per
/// layer and per tile, at the 95th percentile of the sampled tiles, and stand for ADR-002's gate (a tile drawn in
/// &lt; 50 ms at p95, a z8 viewport ≤ ~1.8 MB on the wire, i.e. ≤ ~45 kB for each of its ~40 tiles): a vector tile's
/// drawing time grows with the vertices it simplifies and clips, its bytes with the features it carries; a raster
/// metatile's time and PNG bytes with the area it fills and strokes, which for lines and polygons follows their vertices.
/// The vertex defaults come from <c>docs/perf/render-baseline.md</c> ("After XD01-439"): on the benchmark machine a vector
/// tile cost ~2–5 µs per vertex (≈ 75 ms at p95 for a tile of ~14,000 polygon vertices, 22 ms for ~14,000 line vertices),
/// and a raster metatile of the same layers cost as much or more and weighed 1.5–2.8 MB per viewport — so the raster
/// budget is no looser than the vector one. A layer's own budget is not the tile's: a tile carrying several dense layers
/// costs their sum. Not validated on construction: <see cref="FindDefect"/> says whether it is valid.
/// </summary>
public sealed record LodCalibrationSettings
{
    /// <summary>The defaults.</summary>
    public static LodCalibrationSettings Default { get; } = new();

    /// <summary>The lowest zoom measured (default 0). Below it the calibration knows nothing, so it recommends the coarsest tier it measured there for those zooms.</summary>
    public int MinZoom { get; init; } = TileAddress.MinZoom;

    /// <summary>The highest zoom measured (default 18, the tile endpoints' <c>MaxCachedZoom</c>); above it a tile holds less than at it.</summary>
    public int MaxZoom { get; init; } = 18;

    /// <summary>The features whose centroids pick the tiles to measure, per asset type (default 512).</summary>
    public int SeedFeatures { get; init; } = 512;

    /// <summary>The tiles measured per asset type and zoom (default 32), among those the seeds fall in.</summary>
    public int SampleTiles { get; init; } = 32;

    /// <summary>The most features read per measured tile (default 20,000): a tile reaching it is saturated, its counts lower bounds. Above every feature budget.</summary>
    public int MaxFeaturesPerTile { get; init; } = 20_000;

    /// <summary>A layer is drawn as vector features at a zoom only if its p95 features per tile are at most this (default 2,000: the bytes a viewport's vector tiles carry).</summary>
    public int VectorMaxFeaturesPerTile { get; init; } = 2_000;

    /// <summary>…and its p95 vertices per tile at most this (default 15,000: what a vector tile simplifies and clips in about its time budget).</summary>
    public long VectorMaxVerticesPerTile { get; init; } = 15_000;

    /// <summary>
    /// A layer is drawn as raster tiles at a zoom (below its vector zooms) only if its p95 features per tile are at most
    /// this (default 2,500: the raster source reads at most 10,000 features per 2 × 2 metatile), else it is aggregated.
    /// </summary>
    public int RasterMaxFeaturesPerTile { get; init; } = 2_500;

    /// <summary>…and its p95 vertices per tile at most this (default 15,000, as the vector budget: a raster metatile is no cheaper to draw, and heavier on the wire).</summary>
    public long RasterMaxVerticesPerTile { get; init; } = 15_000;

    /// <summary>The first setting out of range, as a message naming it; <c>null</c> when every setting is valid.</summary>
    public string? FindDefect()
    {
        if (MinZoom is < TileAddress.MinZoom or > TileAddress.MaxZoom) return Zoom(nameof(MinZoom));
        if (MaxZoom is < TileAddress.MinZoom or > TileAddress.MaxZoom) return Zoom(nameof(MaxZoom));
        if (MinZoom > MaxZoom) return $"{nameof(MinZoom)} must not be above {nameof(MaxZoom)}.";
        if (SeedFeatures is < 1 or > 100_000) return $"{nameof(SeedFeatures)} must be between 1 and 100,000.";
        if (SampleTiles is < 1 or > 1_000) return $"{nameof(SampleTiles)} must be between 1 and 1,000.";
        if (VectorMaxFeaturesPerTile < 1) return Positive(nameof(VectorMaxFeaturesPerTile));
        if (VectorMaxVerticesPerTile < 1) return Positive(nameof(VectorMaxVerticesPerTile));
        if (RasterMaxFeaturesPerTile < 1) return Positive(nameof(RasterMaxFeaturesPerTile));
        if (RasterMaxVerticesPerTile < 1) return Positive(nameof(RasterMaxVerticesPerTile));
        if (MaxFeaturesPerTile is < 1 or > 1_000_000) return $"{nameof(MaxFeaturesPerTile)} must be between 1 and 1,000,000.";
        if (MaxFeaturesPerTile <= Math.Max(VectorMaxFeaturesPerTile, RasterMaxFeaturesPerTile))
            return $"{nameof(MaxFeaturesPerTile)} must be above {nameof(VectorMaxFeaturesPerTile)} and {nameof(RasterMaxFeaturesPerTile)}, or a saturated tile could pass a budget.";
        return null;
    }

    /// <summary>Throws <see cref="InvalidOperationException"/> with <see cref="FindDefect"/>'s message when a setting is invalid.</summary>
    public void EnsureValid()
    {
        if (FindDefect() is { } defect)
            throw new InvalidOperationException($"The LOD calibration settings are invalid: {defect}");
    }

    private static string Zoom(string name) =>
        string.Create(CultureInfo.InvariantCulture, $"{name} must be a zoom in [{TileAddress.MinZoom}, {TileAddress.MaxZoom}].");

    private static string Positive(string name) => $"{name} must be positive.";
}
