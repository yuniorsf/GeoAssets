using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;

namespace GeoAssets.IpBilling.AssetTypes;

/// <summary>
/// The three global, built-in <see cref="AssetType"/>s this epic adds for public-lighting
/// billing — same "stable well-known ID, <see cref="AssetType.IsBuiltIn"/>/<see cref="AssetType.IsProtected"/>
/// both true" convention as <see cref="AssetType.Point"/>/<see cref="AssetType.Line"/>/
/// <see cref="AssetType.Area"/>. The IDs continue the existing seeded-asset-type numbering
/// (<c>...0004</c> through <c>...0008</c> are already taken by
/// <c>SeedDomainAssetTypesAndLayers</c> in <c>GeoAssets.Provider.PostgreSQL</c>), and are
/// seeded into Postgres by that same migration-based convention — see
/// <c>SeedIpBillingAssetTypes</c>.
///
/// Per the parent epic (XD01-175): Transformer and Pole are real <see cref="GeoFeature"/>s
/// (<c>TopoEdge(Kind="feeds")</c> Transformer→Pole, mirroring
/// <c>examples/Topology/01_ElectricCircuit.cs</c>); LampGroup24h is its own feature too,
/// since 24h-irregular lamp groups are tracked as a standing entity across multiple field
/// visits. Normal-lamp breakdown is plain relational data, not a feature, since the source
/// data is always an aggregate count.
/// </summary>
public static class IpBillingAssetTypes
{
    public static readonly AssetType Transformador = new()
    {
        Id = Guid.Parse("00000000-0000-0000-0000-000000000009"),
        Name = "Transformador (IP)",
        Color = "#e67e22",
        IsBuiltIn = true,
        IsProtected = true,
        AllowedGeometryType = GeometryType.Point,
    };

    public static readonly AssetType Poste = new()
    {
        Id = Guid.Parse("00000000-0000-0000-0000-00000000000a"),
        Name = "Poste (IP)",
        Color = "#8b5a2b",
        IsBuiltIn = true,
        IsProtected = true,
        AllowedGeometryType = GeometryType.Point,
    };

    public static readonly AssetType LampGroup24h = new()
    {
        Id = Guid.Parse("00000000-0000-0000-0000-00000000000b"),
        Name = "LampGroup24h (IP)",
        Color = "#f1c40f",
        IsBuiltIn = true,
        IsProtected = true,
        AllowedGeometryType = GeometryType.Point,
    };

    public static IEnumerable<AssetType> Defaults => [Transformador, Poste, LampGroup24h];
}
