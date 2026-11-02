namespace GeoAssets.FieldService.Server;

internal sealed record PoleLegalDefenseDossierRequest(IReadOnlyList<string> PoleFeatureIds, DateTimeOffset ValidInstallationTimestampUtc);
