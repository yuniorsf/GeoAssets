namespace GeoAssets.Core.Rendering;

/// <summary>
/// The pure part of the LOD calibration (ADR-002 D6, XD01-439): which tiles to measure, whether a measured zoom fits a
/// tier's budgets, and the <see cref="LodThresholds"/> the measured zooms call for. No I/O — the same answer for the same
/// arguments.
/// </summary>
public static class LodCalibration
{
    /// <summary>
    /// Whether a layer of density <paramref name="density"/> may be drawn as vector features: its p95 features and
    /// vertices per tile are within <paramref name="settings"/>' vector budgets (a zoom without a sampled tile always fits).
    /// </summary>
    public static bool FitsVector(TileDensityAtZoom density, LodCalibrationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(density);
        ArgumentNullException.ThrowIfNull(settings);

        return density.SampledTiles == 0
            || (density.Features.P95 <= settings.VectorMaxFeaturesPerTile && density.Vertices.P95 <= settings.VectorMaxVerticesPerTile);
    }

    /// <summary>Whether a layer of density <paramref name="density"/> may be drawn as raster tiles: within the raster budgets.</summary>
    public static bool FitsRaster(TileDensityAtZoom density, LodCalibrationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(density);
        ArgumentNullException.ThrowIfNull(settings);

        return density.SampledTiles == 0
            || (density.Features.P95 <= settings.RasterMaxFeaturesPerTile && density.Vertices.P95 <= settings.RasterMaxVerticesPerTile);
    }

    /// <summary>
    /// The tiles of <paramref name="zoom"/> to measure: those <paramref name="seeds"/> fall in, each once; when there are
    /// more than <paramref name="maxTiles"/>, a deterministic pseudo-random choice of them (by a hash of the tile, so the
    /// same seeds always pick the same tiles). Ordered by column, then row. A tile holding more features is likelier to be
    /// picked (more seeds fall in it), which leans the percentiles towards the denser tiles — the conservative side.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="zoom"/> is outside the pyramid or <paramref name="maxTiles"/> is not positive.</exception>
    public static IReadOnlyList<TileAddress> SampleTiles(IEnumerable<TileDensitySeed> seeds, int zoom, int maxTiles)
    {
        ArgumentNullException.ThrowIfNull(seeds);
        TileAddress.EnsureZoom(zoom, nameof(zoom));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxTiles);

        var tiles = seeds
            .Where(s => double.IsFinite(s.Longitude) && double.IsFinite(s.Latitude))
            .Select(s => TileAddress.Containing(s.Longitude, s.Latitude, zoom))
            .Distinct()
            .ToList();

        IEnumerable<TileAddress> chosen = tiles.Count <= maxTiles ? tiles : tiles.OrderBy(Hash).Take(maxTiles);
        return [.. chosen.OrderBy(t => t.X).ThenBy(t => t.Y)];
    }

    /// <summary>
    /// The thresholds <paramref name="densities"/> call for. <paramref name="densities"/> are one asset type's measured
    /// zooms — a contiguous run, in any order, as <see cref="LodCalibrator"/> measures them (from its highest drawn zoom
    /// down, stopping at the first that fits no tier). Going down from the highest: <c>VectorFromZoom</c> is the lowest
    /// zoom such that it and every measured zoom above it <see cref="FitsVector">fit the vector budgets</see>
    /// (one above the highest when the highest does not); <c>RasterFromZoom</c> the lowest zoom below that such that every
    /// measured zoom down to it <see cref="FitsRaster">fits the raster budgets</see>; below it, aggregates. When every
    /// measured zoom fits and the lowest measured is the lowest the type is drawn at
    /// (<paramref name="typeMinZoom"/>, else 0), nothing below needs a coarser tier: raster from 0, or
    /// <see cref="LodThresholds.VectorOnly"/> when every zoom fits as vector. The visibility bounds are left open (the
    /// asset type's own zoom range still applies). No measured zoom: <see cref="LodThresholds.VectorOnly"/>.
    /// </summary>
    /// <exception cref="ArgumentException">The zooms are not a contiguous run without repeats.</exception>
    public static LodThresholds Recommend(IReadOnlyCollection<TileDensityAtZoom> densities, LodCalibrationSettings settings, int? typeMinZoom = null)
    {
        ArgumentNullException.ThrowIfNull(densities);
        ArgumentNullException.ThrowIfNull(settings);
        if (densities.Count == 0) return LodThresholds.VectorOnly;

        var descending = densities.OrderByDescending(d => d.Zoom).ToArray();
        for (var i = 1; i < descending.Length; i++)
        {
            if (descending[i].Zoom != descending[i - 1].Zoom - 1)
                throw new ArgumentException("The measured zooms must be a contiguous run without repeats.", nameof(densities));
        }

        var highest = descending[0].Zoom;
        var lowest = descending[^1].Zoom;

        var vector = 0;
        while (vector < descending.Length && FitsVector(descending[vector], settings)) vector++;
        var raster = vector;
        while (raster < descending.Length && FitsRaster(descending[raster], settings)) raster++;

        var vectorFrom = vector == 0 ? Math.Min(highest + 1, TileAddress.MaxZoom) : descending[vector - 1].Zoom;
        var rasterFrom = raster == vector ? vectorFrom : descending[raster - 1].Zoom;

        var lowestDrawn = Math.Max(TileAddress.MinZoom, typeMinZoom ?? TileAddress.MinZoom);
        if (raster == descending.Length && lowest <= lowestDrawn)
        {
            if (vector == descending.Length) return LodThresholds.VectorOnly;
            rasterFrom = TileAddress.MinZoom;
        }

        return new LodThresholds(Math.Min(rasterFrom, vectorFrom), vectorFrom);
    }

    private static ulong Hash(TileAddress tile)
    {
        // SplitMix64's finalizer over the column and row: a fixed, well-mixed order independent of the runtime's hashing.
        var z = ((ulong)(uint)tile.X << 32) | (uint)tile.Y;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }
}
