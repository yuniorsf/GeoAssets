namespace GeoAssets.Core.Rendering;

/// <summary>
/// The density-based LOD calibration (ADR-002 D6, XD01-439): for one asset type in the caller's scope, samples
/// <see cref="LodCalibrationSettings.SeedFeatures"/> of its features once, then, from the highest zoom it is drawn at
/// (within <see cref="LodCalibrationSettings.MinZoom"/>…<see cref="LodCalibrationSettings.MaxZoom"/> and the type's own
/// zoom range) down, measures the features and vertices of up to <see cref="LodCalibrationSettings.SampleTiles"/> of the
/// tiles those seeds fall in (<see cref="ITileDensitySampler.MeasureAsync"/>, each read capped at
/// <see cref="LodCalibrationSettings.MaxFeaturesPerTile"/>), and stops at the first zoom that fits no tier's budgets —
/// a tile at a lower zoom covers four of the zoom above, so it is denser still and would be aggregated anyway. The
/// recommendation is <see cref="LodCalibration.Recommend"/>'s. Reads are sequential, bounded per call, and the token
/// is checked before each; nothing is applied — a recommendation is a configuration change for an operator to make.
/// </summary>
public sealed class LodCalibrator
{
    private readonly ITileDensitySampler _sampler;

    /// <exception cref="InvalidOperationException"><paramref name="settings"/> are invalid.</exception>
    public LodCalibrator(ITileDensitySampler sampler, LodCalibrationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(sampler);
        ArgumentNullException.ThrowIfNull(settings);
        settings.EnsureValid();

        _sampler = sampler;
        Settings = settings;
    }

    /// <summary>The sampling and the budgets.</summary>
    public LodCalibrationSettings Settings { get; }

    /// <summary>
    /// Calibrates <paramref name="assetTypeId"/>, drawn from <paramref name="typeMinZoom"/> to
    /// <paramref name="typeMaxZoom"/> (inclusive; <c>null</c> = open), in the sampler's scope.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="assetTypeId"/> is <c>null</c> or blank.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled.</exception>
    public async Task<AssetTypeLodCalibration> CalibrateAsync(string assetTypeId, int? typeMinZoom, int? typeMaxZoom, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetTypeId);
        ct.ThrowIfCancellationRequested();

        var highest = Math.Min(Settings.MaxZoom, typeMaxZoom ?? TileAddress.MaxZoom);
        var lowest = Math.Max(Settings.MinZoom, typeMinZoom ?? TileAddress.MinZoom);
        if (highest < lowest) return new AssetTypeLodCalibration(assetTypeId, 0, [], null);

        var seeds = await _sampler.SampleSeedsAsync(assetTypeId, Settings.SeedFeatures, ct).ConfigureAwait(false);
        if (seeds.Count == 0) return new AssetTypeLodCalibration(assetTypeId, 0, [], null);

        var densities = new List<TileDensityAtZoom>(highest - lowest + 1);
        for (var zoom = highest; zoom >= lowest; zoom--)
        {
            ct.ThrowIfCancellationRequested();
            var tiles = LodCalibration.SampleTiles(seeds, zoom, Settings.SampleTiles);
            var samples = await _sampler.MeasureAsync(assetTypeId, tiles, Settings.MaxFeaturesPerTile, ct).ConfigureAwait(false);
            var density = TileDensityAtZoom.From(zoom, samples);
            densities.Add(density);

            if (!LodCalibration.FitsVector(density, Settings) && !LodCalibration.FitsRaster(density, Settings)) break;
        }

        return new AssetTypeLodCalibration(assetTypeId, seeds.Count, densities, LodCalibration.Recommend(densities, Settings, typeMinZoom));
    }
}
