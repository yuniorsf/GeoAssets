namespace GeoAssets.Shared.Components.Assets;

public sealed record SchemaField(
    string Key,
    string Label,
    SchemaFieldKind Kind,
    bool Required,
    double? Minimum,
    double? Maximum,
    IReadOnlyList<string>? EnumValues);
