using System.Text.Json;
using NetTopologySuite.Geometries;

namespace GeoAssets.Infrastructure.PostgreSQL.Entities;

/// <summary>
/// EF Core entity that maps to the <c>geo_entity</c> table.
/// All geospatial data is stored in a PostGIS geometry column (SRID 4326).
/// Flexible properties (custom attributes, topology) are stored as JSONB columns.
/// </summary>
public sealed class GeoEntityRow
{
    public string Id            { get; set; } = string.Empty;
    public string Name          { get; set; } = string.Empty;
    public string AssetTypeId   { get; set; } = string.Empty;
    public string Description   { get; set; } = string.Empty;
    public string LayerId       { get; set; } = string.Empty;
    public DateTime CreatedAt   { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt   { get; set; } = DateTime.UtcNow;

    /// <summary>PostGIS geometry column via Npgsql.NetTopologySuite (SRID 4326).</summary>
    public Geometry? Geom { get; set; }

    /// <summary>JSONB — serialized Dictionary&lt;string,string&gt;</summary>
    public string CustomAttributesJson { get; set; } = "{}";

    /// <summary>JSONB — serialized List&lt;TopoEdgeRow&gt;</summary>
    public string TopologyJson { get; set; } = "[]";

    // ── Navigation ──────────────────────────────────────────────────────────────
    public AssetTypeRow? AssetType { get; set; }
}
