namespace GeoAssets.FieldService.Server;

internal sealed record IspComplianceDossierRequest(string DistributorName, IReadOnlyList<string> PoleFeatureIds);
