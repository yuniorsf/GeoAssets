namespace GeoAssets.Core.FeatureImport;

/// <summary>
/// Well-known <c>CustomAttributes</c> keys of imported features (provenance). <see cref="Scope"/> and
/// <see cref="ImportId"/> are stamped by the kernel on every chunk write; the others are set by the importer.
/// </summary>
public static class FeatureImportAttributes
{
    public const string Scope = "source.scope";
    public const string ImportId = "source.importId";
    public const string Profile = "source.profile";
    public const string Key = "source.key";
    public const string Completeness = "import.completeness";
    public const string PositionAccuracy = "import.positionAccuracy";
}
