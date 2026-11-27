using System.IO.Compression;
using GeoAssets.Core.Rendering;
using Npgsql;
using NpgsqlTypes;

namespace GeoAssets.Bench.Render;

/// <summary>One tile as <see cref="TileEndpointPipeline"/> served it.</summary>
/// <param name="Mvt">The vector tile's bytes as the source drew them (empty for an empty tile).</param>
/// <param name="WireBytes">The body on the wire: the gzip-encoded tile, 0 for an empty tile (<c>204 No Content</c>).</param>
/// <param name="Database">Executing the tile statement.</param>
/// <param name="Total">The source's statement plus the endpoint's gzip encoding.</param>
internal sealed record TileResponse(byte[] Mvt, long WireBytes, TimeSpan Database, TimeSpan Total);

/// <summary>
/// The kernel's vector tile path (XD01-434), reproduced in-process for an organization-less caller under an LOD policy
/// (the default: vector at every zoom; XD01-439 runs it under the calibrated one): <c>GET /tiles/assets/{v}/{z}/{x}/{y}.mvt</c>
/// asks <c>PostgresVectorTileSource</c> for the asset types drawn at the tile's zoom whose tier there is vector or aggregate
/// (<see cref="LayersAt"/>: the endpoint's <c>LayerSetAt</c> — types outside their zoom range, and raster-tier types, which
/// travel as <c>.png</c> tiles, are left out before the source is asked; the bench's types have no geometry restriction, so
/// they are in name order), the source aggregating those the policy calls aggregate, which runs <see cref="TileSql"/> — a copy of the source's statement, kept equal by
/// <c>tests/GeoAssets.Bench.Render.Tests</c>, which compares the bytes — and the endpoint gzip-encodes the tile once at
/// <see cref="CompressionLevel.Optimal"/> before caching it. The bench cannot call the source or the endpoint itself
/// (Invariant 1). Not included: Kestrel, authentication/authorization (<c>features:read</c>), the grant lookup of the cache
/// scope, the gateway hop.
/// </summary>
internal sealed class TileEndpointPipeline(NpgsqlDataSource dataSource, TimeProvider timeProvider, ILodPolicy? lod = null, TileFeatureBudget? budget = null)
{
    /// <summary><c>PostgresVectorTileSource.Extent</c>.</summary>
    public const int Extent = 4096;

    /// <summary><c>PostgresVectorTileSource.Buffer</c>.</summary>
    public const int Buffer = 64;

    /// <summary><c>PostgresVectorTileSource.AggregateGridSize</c>.</summary>
    public const int AggregateGridSize = 64;

    private static readonly string[] ReservedProperties = ["id", "name", "assetTypeId", "layerId"];

    internal const string TileSql = """
        WITH tile AS (
            SELECT t.envelope, ST_XMin(t.envelope) AS xmin, ST_YMin(t.envelope) AS ymin,
                   ST_XMax(t.envelope) AS xmax, ST_YMax(t.envelope) AS ymax,
                   ST_TileEnvelope(@z, @x, @y, margin => @margin) AS clip
            FROM (SELECT ST_TileEnvelope(@z, @x, @y) AS envelope) AS t
        ),
        hidden AS (
            SELECT coalesce(array_agg(t."Id"::text), '{}') AS layers
            FROM asset_type t
            WHERE t."Id"::text = ANY(@layers) AND (t."MinZoom" > @z OR t."MaxZoom" < @z)
        ),
        rule_keys AS (
            SELECT r."AssetTypeId"::text AS layer, array_agg(DISTINCT c."Attribute") AS keys
            FROM layer_rule r
            JOIN layer_rule_condition c ON c."LayerRuleId" = r."Id"
            WHERE r."AssetTypeId"::text = ANY(@discrete)
            GROUP BY r."AssetTypeId"
        ),
        discrete AS (
            SELECT c.ord, c.layer, c."Id", c."Name", c."LayerId", c.custom_attributes, c.geom_3857
            FROM (
                SELECT array_position(@layers, e."AssetTypeId") AS ord, e."AssetTypeId" AS layer,
                       e."Id", e."Name", e."LayerId", e.custom_attributes, e.geom_3857,
                       row_number() OVER (PARTITION BY e."AssetTypeId" ORDER BY e."Id" COLLATE "C") AS rank
                FROM geo_entity e
                CROSS JOIN tile
                CROSS JOIN hidden
                WHERE e."AssetTypeId" = ANY(@discrete)
                  AND e."AssetTypeId" <> ALL(hidden.layers)
                  AND e."OrganizationId" = ANY(@organizations)
                  AND e.geom_3857 && tile.clip
            ) AS c
            WHERE c.rank <= (@layerBudgets)[array_position(@discrete, c.layer)]
            ORDER BY c.ord, c."Id" COLLATE "C"
            LIMIT @budget
        ),
        discrete_features AS MATERIALIZED (
            SELECT d.ord, d.layer, d."Id" AS id, d."Name" AS name, d.layer AS "assetTypeId", d."LayerId"::text AS "layerId",
                   (SELECT jsonb_object_agg(kv.key, kv.value)
                    FROM jsonb_each_text(d.custom_attributes) AS kv
                    WHERE kv.key = ANY(rk.keys) AND kv.key <> ALL(@reserved)) AS attributes,
                   ST_AsMVTGeom(ST_Simplify(d.geom_3857, @tolerance, true), tile.envelope, @extent, @buffer, true) AS geom
            FROM discrete d
            CROSS JOIN tile
            LEFT JOIN rule_keys rk ON rk.layer = d.layer
        ),
        aggregate_cells AS (
            SELECT array_position(@layers, e."AssetTypeId") AS ord, e."AssetTypeId" AS layer, count(*)::int AS count,
                   avg(ST_X(e.geom_3857_centroid)) AS x, avg(ST_Y(e.geom_3857_centroid)) AS y
            FROM geo_entity e
            CROSS JOIN tile
            CROSS JOIN hidden
            WHERE e."AssetTypeId" = ANY(@aggregate)
              AND e."AssetTypeId" <> ALL(hidden.layers)
              AND e."OrganizationId" = ANY(@organizations)
              AND e.geom_3857_centroid && tile.envelope
              AND ST_X(e.geom_3857_centroid) >= tile.xmin AND ST_X(e.geom_3857_centroid) < tile.xmax
              AND ST_Y(e.geom_3857_centroid) >= tile.ymin AND ST_Y(e.geom_3857_centroid) < tile.ymax
            GROUP BY e."AssetTypeId", floor((ST_X(e.geom_3857_centroid) - tile.xmin) / @cell), floor((ST_Y(e.geom_3857_centroid) - tile.ymin) / @cell)
        ),
        aggregate_features AS (
            SELECT a.ord, a.layer, a.layer AS "assetTypeId", a.count,
                   ST_AsMVTGeom(ST_SetSRID(ST_MakePoint(a.x, a.y), 3857), tile.envelope, @extent, @buffer, true) AS geom
            FROM aggregate_cells a
            CROSS JOIN tile
        ),
        layers AS (
            SELECT f.ord, ST_AsMVT(p, f.layer, @extent, 'geom' ORDER BY f.id COLLATE "C") AS mvt
            FROM discrete_features f
            CROSS JOIN LATERAL (SELECT f.id, f.name, f."assetTypeId", f."layerId", f.attributes, f.geom) AS p
            WHERE f.geom IS NOT NULL
            GROUP BY f.ord, f.layer
            UNION ALL
            SELECT f.ord, ST_AsMVT(p, f.layer, @extent, 'geom') AS mvt
            FROM aggregate_features f
            CROSS JOIN LATERAL (SELECT f."assetTypeId", f.count, f.geom) AS p
            WHERE f.geom IS NOT NULL
            GROUP BY f.ord, f.layer
        )
        SELECT string_agg(mvt, ''::bytea ORDER BY ord) FROM layers
        """;

    /// <summary>The policy (vector at every zoom unless one is given).</summary>
    public ILodPolicy Lod { get; } = lod ?? new LodPolicy(LodThresholds.VectorOnly);

    /// <summary>The discrete-feature budget (the source's default unless one is given: <c>GeoAssets:Tiles:Budget</c>, XD01-457).</summary>
    public TileFeatureBudget Budget { get; } = budget ?? TileFeatureBudget.Default;

    /// <summary>
    /// The layers a vector tile at <paramref name="zoom"/> of the <c>assets</c> set carries under <paramref name="lod"/>
    /// (default: vector at every zoom): the types drawn there as vector features or aggregates, in the endpoint's order.
    /// </summary>
    public static IReadOnlyList<string> LayersAt(int zoom, ILodPolicy? lod = null) =>
        [.. DatasetComposition.AssetTypes
            .Where(t => t.IsDrawnAt(zoom))
            .Where(t => lod is null || lod.Decide(t.Id.ToString("D"), zoom) is TileRenderTier.Vector or TileRenderTier.Aggregate)
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ThenBy(t => t.Id)
            .Select(t => t.Id.ToString("D"))];

    public async Task<TileResponse> GetAsync(TileAddress address, CancellationToken cancellationToken)
    {
        var started = timeProvider.GetTimestamp();
        var layers = LayersAt(address.Z, Lod);
        if (layers.Count == 0) return new TileResponse([], 0, TimeSpan.Zero, timeProvider.GetElapsedTime(started));

        byte[] mvt;
        var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await using (transaction.ConfigureAwait(false))
            {
                var command = new NpgsqlCommand(TileSql, connection, transaction);
                await using (command.ConfigureAwait(false))
                {
                    AddParameters(command, address, layers, [.. layers.Where(id => Lod.Decide(id, address.Z) == TileRenderTier.Aggregate)]);
                    mvt = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as byte[] ?? [];
                }

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        var database = timeProvider.GetElapsedTime(started);
        var wire = mvt.Length == 0 ? 0 : Gzip(mvt).LongLength;
        return new TileResponse(mvt, wire, database, timeProvider.GetElapsedTime(started));
    }

    /// <summary>The endpoint's encoding of a drawn vector tile (<c>TileEndpointExtensions.Encode</c>).</summary>
    internal static byte[] Gzip(byte[] content)
    {
        using var buffer = new MemoryStream();
        using (var gzip = new GZipStream(buffer, CompressionLevel.Optimal, leaveOpen: true))
            gzip.Write(content);
        return buffer.ToArray();
    }

    private void AddParameters(NpgsqlCommand command, TileAddress address, IReadOnlyList<string> layers, IReadOnlyList<string> aggregate)
    {
        var tileWidth = address.Envelope3857.Width;
        var parameters = command.Parameters;
        parameters.Add(new NpgsqlParameter("z", NpgsqlDbType.Integer) { Value = address.Z });
        parameters.Add(new NpgsqlParameter("x", NpgsqlDbType.Integer) { Value = address.X });
        parameters.Add(new NpgsqlParameter("y", NpgsqlDbType.Integer) { Value = address.Y });
        parameters.Add(new NpgsqlParameter("margin", NpgsqlDbType.Double) { Value = (double)Buffer / Extent });
        parameters.Add(new NpgsqlParameter("extent", NpgsqlDbType.Integer) { Value = Extent });
        parameters.Add(new NpgsqlParameter("buffer", NpgsqlDbType.Integer) { Value = Buffer });
        parameters.Add(new NpgsqlParameter("tolerance", NpgsqlDbType.Double) { Value = tileWidth / Extent });
        parameters.Add(new NpgsqlParameter("cell", NpgsqlDbType.Double) { Value = tileWidth / AggregateGridSize });
        parameters.Add(new NpgsqlParameter("budget", NpgsqlDbType.Integer) { Value = Budget.MaxFeaturesPerTile });
        parameters.Add(new NpgsqlParameter("organizations", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = new[] { Guid.Empty } });
        parameters.Add(TextArray("layers", layers));
        string[] discrete = [.. layers.Except(aggregate, StringComparer.Ordinal)];
        parameters.Add(TextArray("discrete", discrete));
        parameters.Add(new NpgsqlParameter("layerBudgets", NpgsqlDbType.Array | NpgsqlDbType.Integer) { Value = discrete.Select(Budget.ForLayer).ToArray() });
        parameters.Add(TextArray("aggregate", aggregate));
        parameters.Add(TextArray("reserved", ReservedProperties));
    }

    private static NpgsqlParameter TextArray(string name, IReadOnlyCollection<string> values) =>
        new(name, NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = values.ToArray() };
}
