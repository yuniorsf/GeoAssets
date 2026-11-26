using System.IO.Compression;
using System.Text.Json;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Services;
using NetTopologySuite.Geometries;
using Npgsql;

namespace GeoAssets.Bench.Render;

/// <summary>One <c>GET /features/bounds</c> response as <see cref="BoundsEndpointPipeline"/> produced it.</summary>
/// <param name="Payload">The UTF-8 response body, uncompressed.</param>
/// <param name="BrotliPayload">The body as the kernel sends it to a caller that accepts Brotli (every browser).</param>
/// <param name="MatchedRows">Rows the read's filter matches (bbox, scope and the zoom's asset types), before its <c>LIMIT</c> — counted apart, untimed.</param>
/// <param name="ReturnedFeatures">Features in the body: the matches up to the <c>LIMIT</c>.</param>
/// <param name="Database">Executing the two queries and reading the returned rows.</param>
/// <param name="Total">The whole pipeline: queries, read, map, serialize, Brotli.</param>
internal sealed record BoundsResponse(
    byte[] Payload, byte[] BrotliPayload, int MatchedRows, int ReturnedFeatures, TimeSpan Database, TimeSpan Total);

/// <summary>
/// The kernel's viewport read, reproduced in-process: what <c>GET /api/geoassets/features/bounds?…&amp;zoom=N</c> does
/// through <c>PostgresAssetProvider.GetInViewportAsync</c> since XD01-430 — the asset types hidden at the zoom
/// (<see cref="HiddenTypesSql"/>), then the <c>geo_entity</c> rows of the caller's scope (here the organization-less one,
/// <see cref="Guid.Empty"/>) not of those types whose geometry intersects the bbox, the first
/// <see cref="MaxBoundsFeatureCount"/> by ordinal id (<see cref="Sql"/>: <c>ORDER BY … COLLATE "C" LIMIT</c>), mapped
/// to <see cref="GeoFeature"/>, serialized with <see cref="GeoJsonSerializer.CompactOptions"/> and Brotli-compressed at
/// <see cref="CompressionLevel.Fastest"/> (the kernel's response compression). The XD01-420 baseline read every
/// intersecting row, mapped them all, cut them at the cap and sent the indented body uncompressed. The bench cannot call
/// the provider itself (Invariant 1: only <c>GeoAssets.Server</c> references <c>GeoAssets.Provider.PostgreSQL</c>);
/// <c>tests/GeoAssets.Bench.Render.Tests</c> asserts both produce the same body. Not included: Kestrel,
/// authentication/authorization, the gateway hop, and EF Core's materializer overhead.
/// </summary>
internal sealed class BoundsEndpointPipeline(NpgsqlDataSource dataSource, TimeProvider timeProvider)
{
    /// <summary><c>GeoAssetsRestApiExtensions.MaxBoundsFeatureCount</c>.</summary>
    public const int MaxBoundsFeatureCount = 10_000;

    /// <summary>The query EF Core issues for the zoom's hidden asset types (<c>ZoomRangeQueries.HiddenAssetTypeIdsAsync</c>).</summary>
    internal const string HiddenTypesSql = """
        SELECT a."Id"
        FROM asset_type AS a
        WHERE a."MinZoom" > @zoom OR a."MaxZoom" < @zoom
        """;

    /// <summary>The filter of <see cref="Sql"/>: the tenant query filter, the hidden types, then <c>Geom.Intersects(bbox)</c>.</summary>
    private const string Filter = """
        WHERE g."OrganizationId" = @organizationId AND NOT (g."AssetTypeId" = ANY (@hidden))
          AND g.geom IS NOT NULL AND ST_Intersects(g.geom, @bbox)
        """;

    /// <summary>The query EF Core issues for <c>GetInViewportAsync</c>'s rows.</summary>
    internal const string Sql = $"""
        SELECT g."Id", g."Name", g."AssetTypeId", g."Description", g."LayerId", g."OrganizationId",
               g."CreatedAt", g."UpdatedAt", g.geom, g.custom_attributes, g.topology
        FROM geo_entity AS g
        {Filter}
        ORDER BY g."Id" COLLATE "C"
        LIMIT @limit
        """;

    private const string CountSql = $"""
        SELECT count(*)::int
        FROM geo_entity AS g
        {Filter}
        """;

    private static readonly JsonSerializerOptions RowJson = new(JsonSerializerDefaults.Web);
    private static readonly GeometryFactory Factory = new(new PrecisionModel(), 4326);

    public async Task<BoundsResponse> GetAsync(GeoBounds bounds, int zoom, CancellationToken cancellationToken)
    {
        var bbox = Factory.ToGeometry(new Envelope(bounds.MinLon, bounds.MaxLon, bounds.MinLat, bounds.MaxLat));
        bbox.SRID = 4326;

        var started = timeProvider.GetTimestamp();
        var hidden = await HiddenAssetTypeIdsAsync(zoom, cancellationToken).ConfigureAwait(false);
        var rows = await ReadAsync(bbox, hidden, cancellationToken).ConfigureAwait(false);
        var database = timeProvider.GetElapsedTime(started);

        var mapped = rows.ConvertAll(MapToFeature);
        var payload = JsonSerializer.SerializeToUtf8Bytes<IReadOnlyList<GeoFeature>>(mapped, GeoJsonSerializer.CompactOptions);
        var brotli = Brotli(payload);
        var total = timeProvider.GetElapsedTime(started);

        var matched = await CountAsync(bbox, hidden, cancellationToken).ConfigureAwait(false);
        return new BoundsResponse(payload, brotli, matched, mapped.Count, database, total);
    }

    /// <summary>The body as ASP.NET Core's <c>BrotliCompressionProvider</c> at <see cref="CompressionLevel.Fastest"/> writes it.</summary>
    internal static byte[] Brotli(byte[] payload)
    {
        using var buffer = new MemoryStream();
        using (var brotli = new BrotliStream(buffer, CompressionLevel.Fastest, leaveOpen: true))
            brotli.Write(payload);
        return buffer.ToArray();
    }

    private async Task<string[]> HiddenAssetTypeIdsAsync(int zoom, CancellationToken cancellationToken)
    {
        var command = dataSource.CreateCommand(HiddenTypesSql);
        await using (command.ConfigureAwait(false))
        {
            command.Parameters.AddWithValue("zoom", zoom);
            var ids = new List<string>();
            var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            await using (reader.ConfigureAwait(false))
            {
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    ids.Add(reader.GetGuid(0).ToString());
            }

            return [.. ids];
        }
    }

    private async Task<List<Row>> ReadAsync(Geometry bbox, string[] hidden, CancellationToken cancellationToken)
    {
        var command = dataSource.CreateCommand(Sql);
        await using (command.ConfigureAwait(false))
        {
            command.CommandTimeout = 300;
            AddFilterParameters(command, bbox, hidden);
            command.Parameters.AddWithValue("limit", MaxBoundsFeatureCount);

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

    private async Task<int> CountAsync(Geometry bbox, string[] hidden, CancellationToken cancellationToken)
    {
        var command = dataSource.CreateCommand(CountSql);
        await using (command.ConfigureAwait(false))
        {
            command.CommandTimeout = 300;
            AddFilterParameters(command, bbox, hidden);
            return (int)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
        }
    }

    private static void AddFilterParameters(NpgsqlCommand command, Geometry bbox, string[] hidden)
    {
        command.Parameters.AddWithValue("organizationId", Guid.Empty);
        command.Parameters.AddWithValue("hidden", hidden);
        command.Parameters.AddWithValue("bbox", bbox);
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
