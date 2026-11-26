using System.Text.Json;
using GeoAssets.Core.Models;
using GeoAssets.Provider.PostgreSQL.Entities;

namespace GeoAssets.Provider.PostgreSQL.Repositories;

/// <summary>
/// The one <see cref="GeoFeature"/> → <c>geo_entity</c>/<c>geo_entity_attribute</c> mapping, shared by
/// <see cref="PostgresAssetProvider"/>'s writes and <see cref="PostgresFeatureBulkImporter"/>'s staged copy.
/// </summary>
internal static class GeoEntityRowMapper
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    internal static GeoEntityRow MapToRow(GeoFeature f) =>
        new()
        {
            Id                   = f.Id,
            Name                 = f.Properties.Name,
            AssetTypeId          = f.Properties.AssetTypeId,
            Description          = f.Properties.Description,
            LayerId              = string.IsNullOrEmpty(f.Properties.LayerId) ? null : Guid.Parse(f.Properties.LayerId),
            OrganizationId       = f.Properties.OrganizationId,
            CreatedAt            = f.Properties.CreatedAt,
            UpdatedAt            = f.Properties.UpdatedAt,
            Geom                 = f.Geometry?.NtsGeometry,
            CustomAttributesJson = JsonSerializer.Serialize(f.Properties.CustomAttributes, _json),
            TopologyJson         = JsonSerializer.Serialize(f.Topology, _json)
        };

    internal static IEnumerable<GeoEntityAttributeRow> MapToAttributeRows(GeoFeature feature) =>
        feature.Properties.CustomAttributes.Select(kv =>
            new GeoEntityAttributeRow { EntityId = feature.Id, Key = kv.Key, Value = kv.Value });
}
