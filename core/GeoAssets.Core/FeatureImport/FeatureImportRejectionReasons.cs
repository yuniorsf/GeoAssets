namespace GeoAssets.Core.FeatureImport;

/// <summary>Rejection reason codes of <see cref="FeatureImportRejection.Reason"/>.</summary>
public static class FeatureImportRejectionReasons
{
    public const string ForeignOrganization = "foreign-organization";
    public const string UnknownAssetType = "unknown-asset-type";
    public const string GeometryMismatch = "geometry-mismatch";
    public const string MissingGeometry = "missing-geometry";
    public const string NameTooLong = "name-too-long";
    public const string InvalidId = "invalid-id";
}
