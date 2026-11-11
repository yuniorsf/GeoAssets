using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;

namespace GeoAssets.Core.Bdgd;

/// <summary>
/// The 5 ANEEL BDGD (Base de Dados Geográfica da Distribuidora) layer definitions this ticket
/// names — PONNOT, UNTRD, UNREMT, SEGCON, UC — each an <see cref="AssetType"/> carrying its own
/// <see cref="AssetType.AttributesSchemaJson"/>, validated through the existing
/// <c>GeoFeatureAttributeValidator</c> pipeline against a <see cref="GeoFeature"/>'s
/// <see cref="GeoFeatureProperties.CustomAttributes"/> — "or equivalent attribute-schema
/// definitions" (this ticket's own words) in place of new EF Core entities, since that
/// mechanism already exists and does exactly this.
///
/// <c>COD_ID</c> (BDGD's common identifier field across every layer) is the one attribute given
/// genuinely high confidence here. The ticket names 4 more fields without assigning them to a
/// specific layer — <c>POT_NOM</c>/<c>TEN_PRI</c>/<c>TEN_SEC</c> (nominal power, primary/secondary
/// voltage) are assigned to <see cref="Untrd"/> and <c>FAS_CON</c> (phase connection) to
/// <see cref="Segcon"/> here, matching standard distribution-network data modeling (transformer
/// ratings vs. a conductor segment's phase makeup) — a judgment call, not a verified field-to-
/// layer mapping from ANEEL's own manual. <see cref="Unremt"/> and <see cref="Uc"/> are kept to
/// <c>COD_ID</c> alone rather than inventing further field codes with no real confidence behind
/// them; extend each schema against ANEEL's current "Módulo 8 BDGD" manual before treating any
/// of these as a complete, submission-ready field list.
///
/// <see cref="Ponnot"/>'s own schema was extended for XD01-257 with 3 more fields the ticket's AC
/// names by role rather than code — structural class, height, and daN (traction/load) rating —
/// mapped here to <c>TIP_EST</c>/<c>ALT_TOT</c>/<c>CAP_TRC</c> respectively, the same kind of
/// judgment call as the <see cref="Untrd"/>/<see cref="Segcon"/> field assignments above, not a
/// verified mapping from ANEEL's manual.
/// </summary>
public static class BdgdAssetTypes
{
    private const string CodIdOnlySchema = """
    {
      "type": "object",
      "properties": { "COD_ID": { "type": "string", "minLength": 1 } },
      "required": ["COD_ID"]
    }
    """;

    public static readonly AssetType Ponnot = new()
    {
        Id = BdgdAssetTypeIds.Ponnot,
        Name = "PONNOT",
        IsBuiltIn = true,
        AllowedGeometryType = GeometryType.Point,
        AttributesSchemaJson = """
        {
          "type": "object",
          "properties": {
            "COD_ID":  { "type": "string", "minLength": 1 },
            "TIP_EST": { "type": "string", "minLength": 1 },
            "ALT_TOT": { "type": "number", "minimum": 0 },
            "CAP_TRC": { "type": "number", "minimum": 0 }
          },
          "required": ["COD_ID", "TIP_EST", "ALT_TOT", "CAP_TRC"]
        }
        """,
    };

    public static readonly AssetType Untrd = new()
    {
        Id = BdgdAssetTypeIds.Untrd,
        Name = "UNTRD",
        IsBuiltIn = true,
        AllowedGeometryType = GeometryType.Point,
        AttributesSchemaJson = """
        {
          "type": "object",
          "properties": {
            "COD_ID":  { "type": "string", "minLength": 1 },
            "POT_NOM": { "type": "number", "minimum": 0 },
            "TEN_PRI": { "type": "number", "minimum": 0 },
            "TEN_SEC": { "type": "number", "minimum": 0 }
          },
          "required": ["COD_ID", "POT_NOM", "TEN_PRI", "TEN_SEC"]
        }
        """,
    };

    public static readonly AssetType Unremt = new()
    {
        Id = BdgdAssetTypeIds.Unremt,
        Name = "UNREMT",
        IsBuiltIn = true,
        AllowedGeometryType = GeometryType.Point,
        AttributesSchemaJson = CodIdOnlySchema,
    };

    public static readonly AssetType Segcon = new()
    {
        Id = BdgdAssetTypeIds.Segcon,
        Name = "SEGCON",
        IsBuiltIn = true,
        AllowedGeometryType = GeometryType.LineString,
        AttributesSchemaJson = """
        {
          "type": "object",
          "properties": {
            "COD_ID":  { "type": "string", "minLength": 1 },
            "FAS_CON": { "type": "string", "minLength": 1 }
          },
          "required": ["COD_ID", "FAS_CON"]
        }
        """,
    };

    public static readonly AssetType Uc = new()
    {
        Id = BdgdAssetTypeIds.Uc,
        Name = "UC",
        IsBuiltIn = true,
        AllowedGeometryType = GeometryType.Point,
        AttributesSchemaJson = CodIdOnlySchema,
    };

    public static IEnumerable<AssetType> All => [Ponnot, Untrd, Unremt, Segcon, Uc];
}
