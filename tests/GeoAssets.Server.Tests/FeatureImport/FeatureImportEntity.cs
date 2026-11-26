namespace GeoAssets.Server.Tests.FeatureImport;

internal sealed record FeatureImportEntity(
    string Id,
    string Name,
    string AssetTypeId,
    Guid OrganizationId,
    string CustomAttributesJson,
    string TopologyJson,
    int Srid,
    double X,
    double Y);
