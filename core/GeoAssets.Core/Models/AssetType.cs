using GeoAssets.Core.Interfaces;

namespace GeoAssets.Core.Models;

/// <summary>User-defined asset category (e.g. "Water Tower", "Road", "Survey Area")</summary>
public sealed class AssetType : IOrgOwnedResource
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Color { get; set; } = "#3388ff";
    public string IconUrl { get; set; } = string.Empty;
    public bool IsBuiltIn { get; set; } = false;

    /// <summary>See <see cref="IOrgOwnedResource"/>. Defaults to <see cref="Guid.Empty"/> —
    /// "no organization assigned" (built-in types are never org-scoped).</summary>
    public Guid OrganizationId { get; set; } = Guid.Empty;

    /// <summary>
    /// Optional JSON Schema (draft 2020-12) text validating <see cref="GeoFeatureProperties.CustomAttributes"/>
    /// for features of this asset type. Null or empty means unrestricted — any key/value pairs are
    /// accepted (same "empty = unrestricted" convention as <c>OrderType.AttributesSchemaJson</c>).
    /// Enforced by <c>GeoFeatureAttributeValidator</c>, applied on every write by
    /// <c>ValidatingAssetProvider</c>.
    /// </summary>
    public string? AttributesSchemaJson { get; set; }

    // Built-in default types
    public static readonly AssetType Point = new()
    {
        Id = Guid.Parse("00000000-0000-0000-0000-000000000001"),
        Name = "Punto de interés",
        Color = "#e74c3c",
        IsBuiltIn = true
    };

    public static readonly AssetType Line = new()
    {
        Id = Guid.Parse("00000000-0000-0000-0000-000000000002"),
        Name = "Línea",
        Color = "#3498db",
        IsBuiltIn = true
    };

    public static readonly AssetType Area = new()
    {
        Id = Guid.Parse("00000000-0000-0000-0000-000000000003"),
        Name = "Área",
        Color = "#2ecc71",
        IsBuiltIn = true
    };

    public static IEnumerable<AssetType> Defaults => [Point, Line, Area];
}
