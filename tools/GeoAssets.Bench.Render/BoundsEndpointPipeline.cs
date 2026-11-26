using System.Text.Json;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Services;
using NetTopologySuite.Geometries;
using Npgsql;

namespace GeoAssets.Bench.Render;

/// <summary>One <c>GET /features/bounds</c> response as <see cref="BoundsEndpointPipeline"/> produced it.</summary>
/// <param name="Payload">The UTF-8 response body.</param>
/// <param name="MatchedRows">Rows the bbox query returned (and the server materialized).</param>
/// <param name="ReturnedFeatures">Features in the body, after the endpoint's cap.</param>
/// <param name="Database">Executing the query and reading every row.</param>
/// <param name="Total">The whole pipeline: query, read, map, cap, serialize.</param>
internal sealed record BoundsResponse(byte[] Payload, int MatchedRows, int ReturnedFeatures, TimeSpan Database, TimeSpan Total);

/// <summary>
/// The kernel's current viewport read, reproduced in-process (XD01-420 baseline): what <c>GET /api/geoassets/features/bounds</c>
/// does through <c>PostgresAssetProvider.GetInBoundsAsync</c> — every <c>geo_entity</c> row (of the caller's scope; here the
/// organization-less one, <see cref="Guid.Empty"/>) whose geometry intersects the bbox, mapped to <see cref="GeoFeature"/>,
/// capped at <see cref="MaxBoundsFeatureCount"/> <em>after</em> mapping them all, and serialized with
/// <see cref="GeoJsonSerializer.Options"/> (indented). No zoom reaches the server, so every zoom gets every intersecting
/// row. The bench cannot call the provider itself (Invariant 1: only <c>GeoAssets.Server</c> references
/// <c>GeoAssets.Provider.PostgreSQL</c>); <c>tests/GeoAssets.Bench.Render.Tests</c> asserts both produce the same body.
/// Not included: Kestrel, authentication/authorization, the gateway hop, and EF Core's materializer overhead.
/// </summary>
internal sealed class BoundsEndpointPipeline(NpgsqlDataSource dataSource, TimeProvider timeProvider)
{
    /// <summary><c>GeoAssetsRestApiExtensions.MaxBoundsFeatureCount</c>.</summary>
    public const int MaxBoundsFeatureCount = 10_000;

    /// <summary>The query EF Core issues for <c>GetInBoundsAsync</c> (the tenant query filter, then <c>Geom.Intersects(bbox)</c>).</summary>
    internal const string Sql = """
        SELECT g."Id", g."Name", g."AssetTypeId", g."Description", g."LayerId", g."OrganizationId",
               g."CreatedAt", g."UpdatedAt", g.geom, g.custom_attributes, g.topology
        FROM geo_entity AS g
        WHERE g."OrganizationId" = @organizationId AND g.geom IS NOT NULL AND ST_Intersects(g.geom, @bbox)
        """;

    private static readonly JsonSerializerOptions RowJson = new(JsonSerializerDefaults.Web);
    private static readonly GeometryFactory Factory = new(new PrecisionModel(), 4326);

    public async Task<BoundsResponse> GetAsync(GeoBounds bounds, CancellationToken cancellationToken)
    {
        var started = timeProvider.GetTimestamp();
        var features = await ReadAsync(bounds, cancellationToken).ConfigureAwait(false);
        var database = timeProvider.GetElapsedTime(started);

        var matched = features.Count;
        var mapped = features.ConvertAll(MapToFeature);
        if (mapped.Count > MaxBoundsFeatureCount)
            mapped = [.. mapped.Take(MaxBoundsFeatureCount)];

        var payload = JsonSerializer.SerializeToUtf8Bytes<IReadOnlyList<GeoFeature>>(mapped, GeoJsonSerializer.Options);
        return new BoundsResponse(payload, matched, mapped.Count, database, timeProvider.GetElapsedTime(started));
    }

    private async Task<List<Row>> ReadAsync(GeoBounds bounds, CancellationToken cancellationToken)
    {
        var bbox = Factory.ToGeometry(new Envelope(bounds.MinLon, bounds.MaxLon, bounds.MinLat, bounds.MaxLat));
        bbox.SRID = 4326;

        var command = dataSource.CreateCommand(Sql);
        await using (command.ConfigureAwait(false))
        {
            command.CommandTimeout = 300;
            command.Parameters.AddWithValue("organizationId", Guid.Empty);
            command.Parameters.AddWithValue("bbox", bbox);

            var rows = new List<Row>();
            var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            await using (reader.ConfigureAwait(false))
            {
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    var layerId = await reader.IsDBNullAsync(4, cancellationToken).ConfigureAwait(false) ? (Guid?)null : reader.GetGuid(4);
                    var geom = await reader.IsDBNullAsync(8, cancellationToken).ConfigureAwait(false)
                        ? null
                        : await reader.GetFieldValueAsync<Geometry>(8, cancellationToken).ConfigureAwait(false);
                    rows.Add(new Row(
                        reader.GetString(0),
                        reader.GetString(1),
                        reader.GetString(2),
                        reader.GetString(3),
                        layerId,
                        reader.GetGuid(5),
                        reader.GetDateTime(6),
                        reader.GetDateTime(7),
                        geom,
                        reader.GetString(9),
                        reader.GetString(10)));
                }
            }

            return rows;
        }
    }

    /// <summary><c>PostgresAssetProvider.MapToFeature</c>.</summary>
    private static GeoFeature MapToFeature(Row row) => new()
    {
        Id = row.Id,
        Geometry = row.Geom is null ? null : GeoGeometry.FromNts(row.Geom),
        Topology = JsonSerializer.Deserialize<List<TopoEdge>>(row.TopologyJson, RowJson) ?? [],
        Properties = new GeoFeatureProperties
        {
            Name = row.Name,
            AssetTypeId = row.AssetTypeId,
            Description = row.Description,
            LayerId = row.LayerId?.ToString() ?? string.Empty,
            OrganizationId = row.OrganizationId,
            CreatedAt = row.CreatedAt,
            UpdatedAt = row.UpdatedAt,
            Srid = row.Geom?.SRID ?? 4326,
            CustomAttributes = JsonSerializer.Deserialize<Dictionary<string, string>>(row.CustomAttributesJson, RowJson) ?? [],
        },
    };

    private sealed record Row(
        string Id,
        string Name,
        string AssetTypeId,
        string Description,
        Guid? LayerId,
        Guid OrganizationId,
        DateTime CreatedAt,
        DateTime UpdatedAt,
        Geometry? Geom,
        string CustomAttributesJson,
        string TopologyJson);
}
