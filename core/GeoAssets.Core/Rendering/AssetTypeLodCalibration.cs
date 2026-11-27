namespace GeoAssets.Core.Rendering;

/// <summary>One asset type's calibration (XD01-439).</summary>
/// <param name="AssetTypeId">The asset type (a tile layer id).</param>
/// <param name="Seeds">The sampled features whose tiles were measured (0: none in scope).</param>
/// <param name="Densities">The measured zooms, highest first.</param>
/// <param name="Recommended">The thresholds the densities call for; <c>null</c> when nothing was measured (no feature in scope, or the type is drawn at none of the calibrated zooms).</param>
public sealed record AssetTypeLodCalibration(
    string AssetTypeId, int Seeds, IReadOnlyList<TileDensityAtZoom> Densities, LodThresholds? Recommended);
