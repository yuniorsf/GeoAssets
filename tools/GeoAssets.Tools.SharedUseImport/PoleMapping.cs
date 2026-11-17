namespace GeoAssets.Tools.SharedUseImport;

/// <param name="LegacyPoleId">The legacy <c>Poles.Id</c>.</param>
/// <param name="PoleFeatureId">The <c>GeoFeature.Id</c> of the same pole in GeoAssets.</param>
internal sealed record PoleMapping(int LegacyPoleId, string PoleFeatureId);
