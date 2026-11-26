namespace GeoAssets.Ingestion.Model;

/// <summary>Stable issue codes.</summary>
public static class ImportIssueCodes
{
    public const string MissingSidecar = "missing-sidecar";
    public const string OrphanAttributeTable = "orphan-attribute-table";
    public const string DuplicateFile = "duplicate-file";
    public const string CorruptGeometry = "corrupt-geometry";
    public const string UnsupportedCrs = "unsupported-crs";
    public const string UnmappedRecord = "unmapped-record";
    public const string DuplicateElement = "duplicate-element";
    public const string DanglingReference = "dangling-reference";
    public const string EndpointDistance = "endpoint-distance";
    public const string SelfLoop = "self-loop";
    public const string GeometryMismatch = "geometry-mismatch";
    public const string HelmertFallback = "helmert-fallback";
    public const string Rejected = "rejected";
    public const string AmbiguousAnchor = "ambiguous-anchor";
}
