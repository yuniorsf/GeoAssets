using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models.Geometry;

namespace GeoAssets.Core.Models;

/// <summary>User-defined asset category (e.g. "Water Tower", "Road", "Survey Area")</summary>
public sealed class AssetType : IOrgOwnedResource
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Color { get; set; } = "#3388ff";
    public string IconUrl { get; set; } = string.Empty;
    public bool IsBuiltIn { get; set; } = false;

    /// <summary>
    /// Undeletable, independent of <see cref="IsBuiltIn"/> — a seeded type can still be deleted
    /// (<see cref="IsBuiltIn"/> = true, this = false) while a user-created type could later be
    /// marked protected once other data depends on it. Only the 3 generic defaults
    /// (<see cref="Point"/>/<see cref="Line"/>/<see cref="Area"/>) are protected today.
    /// </summary>
    public bool IsProtected { get; set; } = false;

    /// <summary>Restricts features of this type to one geometry shape. <c>null</c> means any geometry is allowed.</summary>
    public GeometryType? AllowedGeometryType { get; set; }

    /// <summary>
    /// Fallback <see cref="Layer"/> applied to features of this type when no <see cref="LayerRule"/>
    /// matches. <c>null</c> means no default style is configured. Resolution order (including
    /// <see cref="GeoFeatureProperties.LayerId"/>) is implemented in XD01-111.
    /// </summary>
    public Guid? DefaultLayerId { get; set; }

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
        IsBuiltIn = true,
        IsProtected = true
    };

    public static readonly AssetType Line = new()
    {
        Id = Guid.Parse("00000000-0000-0000-0000-000000000002"),
        Name = "Línea",
        Color = "#3498db",
        IsBuiltIn = true,
        IsProtected = true
    };

    public static readonly AssetType Area = new()
    {
        Id = Guid.Parse("00000000-0000-0000-0000-000000000003"),
        Name = "Área",
        Color = "#2ecc71",
        IsBuiltIn = true,
        IsProtected = true
    };

    public static IEnumerable<AssetType> Defaults => [Point, Line, Area];
}
