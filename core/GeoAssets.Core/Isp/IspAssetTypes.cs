using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;

namespace GeoAssets.Core.Isp;

/// <summary>
/// The 5 ISP outside-plant layer definitions XD01-272 names — CTO (optical termination box with
/// splitter port inventory), CEO (optical distribution box), optical cable, PTO
/// (customer-premise optical termination point), and ONT (optical network terminal) — each an
/// <see cref="AssetType"/> carrying its own <see cref="AssetType.AttributesSchemaJson"/>,
/// validated through the existing <c>GeoFeatureAttributeValidator</c> pipeline against a
/// <see cref="GeoFeature"/>'s <see cref="GeoFeatureProperties.CustomAttributes"/> — the same
/// "AssetType + attribute schema, no new EF Core entity" shape as
/// <see cref="GeoAssets.Core.Bdgd.BdgdAssetTypes"/>, since <c>GeoEntityRow</c> already persists
/// any <see cref="GeoFeature"/> generically regardless of asset type. This matches the ticket's
/// own premise — "this is the foundation every other ISP ticket reads/writes against" — and
/// keeps a new vertical's seeding (an <c>asset_type</c> row insert) a later, separate concern
/// from this foundation, same deferral <see cref="GeoAssets.Core.Bdgd.BdgdAssetTypes"/> made.
///
/// <see cref="Cto"/>'s splitter-port occupancy percentage is computed on demand by
/// <see cref="CtoCapacityCalculator"/> rather than stored. <see cref="Pto"/> links back to its
/// feeding <see cref="Cto"/> via <c>parentCtoFeatureId</c>; <see cref="Ont"/> links to the
/// customer's <see cref="Pto"/> via <c>parentPtoFeatureId</c> and carries its FSAN/MAC
/// identifiers — real FTTH topology is splitter box → customer-premise termination point →
/// terminal equipment, so the AC's "link to their parent CTO/customer drop" is satisfied by
/// following PTO→CTO. <see cref="Ceo"/> is left schema-free (an empty/no-schema
/// <see cref="AssetType.AttributesSchemaJson"/> means unrestricted, same convention as
/// <c>IpBillingAssetTypes.Transformador</c>/<c>.Poste</c>) since the ticket gives it no
/// attribute content of its own beyond existing as a <see cref="GeoFeature"/>.
/// </summary>
public static class IspAssetTypes
{
    public static readonly AssetType Cto = new()
    {
        Id = IspAssetTypeIds.Cto,
        Name = "CTO",
        IsBuiltIn = true,
        AllowedGeometryType = GeometryType.Point,
        AttributesSchemaJson = $$"""
        {
          "type": "object",
          "properties": {
            "{{CtoCapacityCalculator.TotalSplitterPortsKey}}":    { "type": "integer", "minimum": 1 },
            "{{CtoCapacityCalculator.OccupiedSplitterPortsKey}}": { "type": "integer", "minimum": 0 }
          },
          "required": ["{{CtoCapacityCalculator.TotalSplitterPortsKey}}", "{{CtoCapacityCalculator.OccupiedSplitterPortsKey}}"]
        }
        """,
    };

    public static readonly AssetType Ceo = new()
    {
        Id = IspAssetTypeIds.Ceo,
        Name = "CEO",
        IsBuiltIn = true,
        AllowedGeometryType = GeometryType.Point,
    };

    public static readonly AssetType OpticalCable = new()
    {
        Id = IspAssetTypeIds.OpticalCable,
        Name = "Optical Cable",
        IsBuiltIn = true,
        AllowedGeometryType = GeometryType.LineString,
        AttributesSchemaJson = """
        {
          "type": "object",
          "properties": {
            "fiberCount": { "type": "integer", "minimum": 1 },
            "cableType":  { "type": "string", "enum": ["Aerial", "Underground", "Duct", "ADSS"] }
          },
          "required": ["fiberCount", "cableType"]
        }
        """,
    };

    public static readonly AssetType Pto = new()
    {
        Id = IspAssetTypeIds.Pto,
        Name = "PTO",
        IsBuiltIn = true,
        AllowedGeometryType = GeometryType.Point,
        AttributesSchemaJson = """
        {
          "type": "object",
          "properties": {
            "parentCtoFeatureId": { "type": "string", "minLength": 1 }
          },
          "required": ["parentCtoFeatureId"]
        }
        """,
    };

    public static readonly AssetType Ont = new()
    {
        Id = IspAssetTypeIds.Ont,
        Name = "ONT",
        IsBuiltIn = true,
        AllowedGeometryType = GeometryType.Point,
        AttributesSchemaJson = $$"""
        {
          "type": "object",
          "properties": {
            "fsan":               { "type": "string", "pattern": "{{OpticalNetworkTerminalPatterns.FsanPattern}}" },
            "macAddress":         { "type": "string", "pattern": "{{OpticalNetworkTerminalPatterns.MacAddressPattern}}" },
            "parentPtoFeatureId": { "type": "string", "minLength": 1 }
          },
          "required": ["fsan", "macAddress", "parentPtoFeatureId"]
        }
        """,
    };

    public static IEnumerable<AssetType> All => [Cto, Ceo, OpticalCable, Pto, Ont];
}
