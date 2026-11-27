using GeoAssets.Core.Rendering;

namespace GeoAssets.Server.Tiles;

/// <summary>One asset type's calibration as <c>GET /tiles/lod-calibration</c> answers it (XD01-439).</summary>
/// <param name="AssetTypeId">The asset type — the tile layer id.</param>
/// <param name="Name">Its name.</param>
/// <param name="MinZoom">Its own lowest zoom (<c>null</c> = open).</param>
/// <param name="MaxZoom">Its own highest zoom (<c>null</c> = open).</param>
/// <param name="Seeds">The sampled features whose tiles were measured.</param>
/// <param name="Current">The thresholds the tiles are cut by now.</param>
/// <param name="Recommended">The thresholds the measured densities call for (with the current visibility bounds kept); <c>null</c> when nothing was measured.</param>
/// <param name="Changed">Whether <paramref name="Recommended"/> would draw the type as another tier at some zoom it is drawn at.</param>
/// <param name="Zooms">The measured densities, highest zoom first.</param>
public sealed record TileLodCalibrationResult(
    string AssetTypeId, string Name, int? MinZoom, int? MaxZoom, int Seeds, LodThresholds Current, LodThresholds? Recommended, bool Changed,
    IReadOnlyList<TileDensityAtZoom> Zooms);
