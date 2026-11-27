using GeoAssets.Core.Rendering;

namespace GeoAssets.Server.Tiles;

/// <summary>The body of <c>GET /tiles/lod-calibration</c> (XD01-439).</summary>
/// <param name="OrganizationId">The organization calibrated — the caller's (its features and the shared ones, as its tiles show them).</param>
/// <param name="Settings">The sampling and the budgets (<c>GeoAssets:Tiles:Calibration</c>).</param>
/// <param name="AssetTypes">Each asset type calibrated, in the tile layer order.</param>
/// <param name="OmittedAssetTypeIds">Asset types beyond <see cref="TileLodCalibrationEndpoint.MaxAssetTypesPerRequest"/>, not calibrated: ask for them with <c>?assetTypes=</c>.</param>
/// <param name="Configuration">The settings that would apply the changed recommendations — a configuration change for an operator to make (nothing is applied).</param>
public sealed record TileLodCalibrationResponse(
    Guid OrganizationId,
    LodCalibrationSettings Settings,
    IReadOnlyList<TileLodCalibrationResult> AssetTypes,
    IReadOnlyList<string> OmittedAssetTypeIds,
    IReadOnlyDictionary<string, string> Configuration);
