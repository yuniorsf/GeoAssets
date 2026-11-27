namespace GeoAssets.Core.Rendering;

/// <summary>
/// One asset type's measured density at one zoom (XD01-439): over the <see cref="SampledTiles"/> tiles that hold at least
/// one of its sampled features, the percentiles of features and vertices per tile.
/// </summary>
/// <param name="Zoom">The zoom measured.</param>
/// <param name="SampledTiles">The tiles measured.</param>
/// <param name="SaturatedTiles">Those whose read stopped at the per-tile cap (their counts are lower bounds).</param>
/// <param name="Features">Features per tile.</param>
/// <param name="Vertices">Vertices per tile.</param>
public sealed record TileDensityAtZoom(int Zoom, int SampledTiles, int SaturatedTiles, DensityPercentiles Features, DensityPercentiles Vertices)
{
    /// <summary>The density at <paramref name="zoom"/> of <paramref name="samples"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="zoom"/> is outside the pyramid.</exception>
    public static TileDensityAtZoom From(int zoom, IReadOnlyCollection<TileDensitySample> samples)
    {
        TileAddress.EnsureZoom(zoom, nameof(zoom));
        ArgumentNullException.ThrowIfNull(samples);

        return new TileDensityAtZoom(
            zoom,
            samples.Count,
            samples.Count(s => s.Saturated),
            DensityPercentiles.Of(samples.Select(s => (long)s.Features)),
            DensityPercentiles.Of(samples.Select(s => s.Vertices)));
    }
}
