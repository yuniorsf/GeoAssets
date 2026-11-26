using NetTopologySuite.Geometries;

namespace GeoAssets.Ingestion.Model;

/// <summary>
/// The format-independent element every source profile maps to. <see cref="Geometry"/> is in the source's native CRS
/// until the pipeline reprojects it. <see cref="AssetClass"/> names an <see cref="AssetClassDefinition"/> of the profile.
/// <see cref="Attributes"/> are already mapped (full field names, canonical keys); unknown codes are kept raw.
/// </summary>
public sealed record CanonicalElement
{
    public required SourceKey Key { get; init; }
    public required ElementKind Kind { get; init; }
    public required string AssetClass { get; init; }
    public required Geometry Geometry { get; init; }
    public required string Scope { get; init; }
    public string? Name { get; init; }
    public Completeness Completeness { get; init; } = Completeness.Full;
    public IReadOnlyDictionary<string, string> Attributes { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);
    public IReadOnlyList<ElementReference> References { get; init; } = [];
}
