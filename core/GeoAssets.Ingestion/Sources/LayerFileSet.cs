namespace GeoAssets.Ingestion.Sources;

/// <summary>
/// The files of one layer in one region of a delivery, as found by the inventory. <see cref="AttributeTablePath"/>
/// may point at a differently named table paired by record count (an orphan <c>.dbf</c>).
/// <see cref="FieldAliases"/> maps the table's (possibly truncated) field names to full names, when known.
/// </summary>
public sealed record LayerFileSet
{
    public required string Layer { get; init; }
    public required string Region { get; init; }
    public string? GeometryPath { get; init; }
    public string? AttributeTablePath { get; init; }
    public string? IndexPath { get; init; }
    public string? CrsWkt { get; init; }
    public string? Encoding { get; init; }
    public long RecordCount { get; init; }
    public LayerVerdict Verdict { get; init; }
    public IReadOnlyDictionary<string, string> FieldAliases { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}
