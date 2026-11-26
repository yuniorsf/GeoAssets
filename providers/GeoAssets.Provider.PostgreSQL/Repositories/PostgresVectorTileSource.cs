using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Rendering;
using GeoAssets.Core.Services;
using GeoAssets.Persistence.Postgres.Interceptors;
using GeoAssets.Provider.PostgreSQL.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace GeoAssets.Provider.PostgreSQL.Repositories;

/// <summary>
/// The PostGIS <see cref="IVectorTileSource"/> (ADR-002 D6, XD01-432): one statement per tile builds the whole Mapbox
/// Vector Tile in PostgreSQL — <c>ST_TileEnvelope</c> over the GiST-indexed <c>geom_3857</c> column,
/// <c>ST_AsMVTGeom</c> (extent <see cref="Extent"/>, buffer <see cref="Buffer"/>, clipped), <c>ST_AsMVT</c> per layer —
/// and answers its bytes.
///
/// <para><b>Layers.</b> A layer id is an asset type id, as features name their type: the MVT layer of that name holds
/// the features of that type, layers in the request's order. A layer <see cref="ILodPolicy"/> calls
/// <see cref="TileRenderTier.Hidden"/> at the tile's zoom is left out, and so is every feature of an asset type whose
/// own zoom range (<c>MinZoom</c>/<c>MaxZoom</c>) excludes that zoom, filtered in the SQL as
/// <see cref="ZoomRangeQueries"/> does. A <see cref="TileRenderTier.Aggregate"/> layer is a grid of
/// <see cref="AggregateGridSize"/>² cells per tile: one point per non-empty cell, at the mean of its features' centroids,
/// counting the features whose centroid falls in it (each feature counts in one tile per zoom). Any other tier draws the
/// features themselves, simplified first (<c>ST_SimplifyPreserveTopology</c>, one tile unit at the tile's zoom).</para>
///
/// <para><b>Properties.</b> A feature carries <c>id</c> (its id — the MVT feature id is numeric, so a map client
/// promotes this property instead), <c>name</c>, <c>assetTypeId</c>, <c>layerId</c> (its style override, when set),
/// and the custom attributes its type's layer rules read (what <see cref="LayerResolver"/> evaluates, so a vector style
/// can resolve the same layer) except one named like those four. An aggregate point carries <c>assetTypeId</c> and
/// <c>count</c>.</para>
///
/// <para><b>Budget.</b> A tile holds at most <see cref="MaxFeaturesPerTile"/> discrete features: the first by layer, then
/// ordinal id (<c>COLLATE "C"</c>), as every other bounded read of this provider orders them.</para>
///
/// <para><b>Scope.</b> Like <see cref="PostgresAssetRenderQuery"/>: the read runs in a transaction stamped by
/// <see cref="TenantTransactionInterceptor"/> for the Row-Level Security policy, on a context built for the request's
/// <see cref="ITenantContext"/>, and the SQL itself keeps the caller's organization's rows and the shared
/// (<see cref="Guid.Empty"/>) ones — the shared ones alone without an organization. Never another organization's.</para>
///
/// <para><b>Cancellation.</b> A request already cancelled throws <see cref="OperationCanceledException"/> before
/// touching the database — never an empty tile, which a cache would keep. A started read is not handed the token (an
/// abort mid-handshake can spoil a pooled connection, see <see cref="PostgresAssetRenderQuery"/>); it runs under its
/// own <see cref="QueryTimeout"/>.</para>
/// </summary>
public sealed class PostgresVectorTileSource : IVectorTileSource
{
    /// <summary>The tile's extent, in MVT units per side.</summary>
    public const int Extent = 4096;

    /// <summary>How far, in MVT units, a geometry is kept past the tile's edge (so lines and symbols meet across tiles).</summary>
    public const int Buffer = 64;

    /// <summary>The cells per side of an aggregate layer's grid.</summary>
    public const int AggregateGridSize = 64;

    /// <summary>The default <see cref="MaxFeaturesPerTile"/>.</summary>
    public const int DefaultMaxFeaturesPerTile = 10_000;

    /// <summary>The longest a tile query may run.</summary>
    public static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(30);

    /// <summary>The properties every discrete feature carries: a custom attribute of the same name is not added.</summary>
    private static readonly string[] ReservedProperties = ["id", "name", "assetTypeId", "layerId"];

    private static readonly ILodPolicy VectorEverywhere = new LodPolicy(LodThresholds.VectorOnly);

    private const string TileSql = """
        WITH tile AS (
            SELECT t.envelope, ST_XMin(t.envelope) AS xmin, ST_YMin(t.envelope) AS ymin,
                   ST_XMax(t.envelope) AS xmax, ST_YMax(t.envelope) AS ymax,
                   ST_TileEnvelope(@z, @x, @y, margin => @margin) AS clip
            FROM (SELECT ST_TileEnvelope(@z, @x, @y) AS envelope) AS t
        ),
        rule_keys AS (
            SELECT r."AssetTypeId"::text AS layer, array_agg(DISTINCT c."Attribute") AS keys
            FROM layer_rule r
            JOIN layer_rule_condition c ON c."LayerRuleId" = r."Id"
            WHERE r."AssetTypeId"::text = ANY(@discrete)
            GROUP BY r."AssetTypeId"
        ),
        discrete AS (
            SELECT array_position(@layers, e."AssetTypeId") AS ord, e."AssetTypeId" AS layer,
                   e."Id", e."Name", e."LayerId", e.custom_attributes, e.geom_3857
            FROM geo_entity e
            CROSS JOIN tile
            WHERE e."AssetTypeId" = ANY(@discrete)
              AND e."OrganizationId" = ANY(@organizations)
              AND e.geom_3857 && tile.clip
              AND NOT EXISTS (
                  SELECT 1 FROM asset_type t
                  WHERE t."Id"::text = e."AssetTypeId" AND (t."MinZoom" > @z OR t."MaxZoom" < @z))
            ORDER BY ord, e."Id" COLLATE "C"
            LIMIT @budget
        ),
        discrete_features AS (
            SELECT d.ord, d.layer, d."Id" AS id, d."Name" AS name, d.layer AS "assetTypeId", d."LayerId"::text AS "layerId",
                   (SELECT jsonb_object_agg(kv.key, kv.value)
                    FROM jsonb_each_text(d.custom_attributes) AS kv
                    WHERE kv.key = ANY(rk.keys) AND kv.key <> ALL(@reserved)) AS attributes,
                   ST_AsMVTGeom(ST_SimplifyPreserveTopology(d.geom_3857, @tolerance), tile.envelope, @extent, @buffer, true) AS geom
            FROM discrete d
            CROSS JOIN tile
            LEFT JOIN rule_keys rk ON rk.layer = d.layer
        ),
        aggregate_cells AS (
            SELECT array_position(@layers, e."AssetTypeId") AS ord, e."AssetTypeId" AS layer, count(*)::int AS count,
                   avg(ST_X(p.point)) AS x, avg(ST_Y(p.point)) AS y
            FROM geo_entity e
            CROSS JOIN tile
            CROSS JOIN LATERAL (SELECT ST_Centroid(e.geom_3857) AS point) AS p
            WHERE e."AssetTypeId" = ANY(@aggregate)
              AND e."OrganizationId" = ANY(@organizations)
              AND e.geom_3857 && tile.envelope
              AND NOT EXISTS (
                  SELECT 1 FROM asset_type t
                  WHERE t."Id"::text = e."AssetTypeId" AND (t."MinZoom" > @z OR t."MaxZoom" < @z))
              AND ST_X(p.point) >= tile.xmin AND ST_X(p.point) < tile.xmax
              AND ST_Y(p.point) >= tile.ymin AND ST_Y(p.point) < tile.ymax
            GROUP BY e."AssetTypeId", floor((ST_X(p.point) - tile.xmin) / @cell), floor((ST_Y(p.point) - tile.ymin) / @cell)
        ),
        aggregate_features AS (
            SELECT a.ord, a.layer, a.layer AS "assetTypeId", a.count,
                   ST_AsMVTGeom(ST_SetSRID(ST_MakePoint(a.x, a.y), 3857), tile.envelope, @extent, @buffer, true) AS geom
            FROM aggregate_cells a
            CROSS JOIN tile
        ),
        layers AS (
            SELECT f.ord, ST_AsMVT(p, f.layer, @extent, 'geom') AS mvt
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

    private readonly DbContextOptions<GeoAssetsDbContext> _dbOptions;
    private readonly ITenantContext _tenantContext;
    private readonly ILodPolicy _lodPolicy;
    private readonly TimeProvider _timeProvider;

    /// <param name="dbOptions">Options for the per-tile contexts. Must not enable a retrying execution strategy: each read runs in an explicit transaction.</param>
    /// <param name="tenantContext">The request's tenant; <c>null</c> = none (the shared features only).</param>
    /// <param name="lodPolicy">Which tier each layer is drawn as; <c>null</c> = every layer as vector features at every zoom.</param>
    /// <param name="timeProvider">The query timeout's clock.</param>
    /// <param name="maxFeaturesPerTile">The tile's discrete-feature budget (<see cref="MaxFeaturesPerTile"/>).</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxFeaturesPerTile"/> is not positive.</exception>
    public PostgresVectorTileSource(
        DbContextOptions<GeoAssetsDbContext> dbOptions,
        ITenantContext? tenantContext,
        ILodPolicy? lodPolicy,
        TimeProvider timeProvider,
        int maxFeaturesPerTile = DefaultMaxFeaturesPerTile)
    {
        ArgumentNullException.ThrowIfNull(dbOptions);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxFeaturesPerTile);

        _tenantContext = tenantContext ?? NullTenantContext.Instance;
        _dbOptions = new DbContextOptionsBuilder<GeoAssetsDbContext>(dbOptions)
            .AddInterceptors(new TenantTransactionInterceptor(_tenantContext))
            .Options;
        _lodPolicy = lodPolicy ?? VectorEverywhere;
        _timeProvider = timeProvider;
        MaxFeaturesPerTile = maxFeaturesPerTile;
    }

    /// <summary>The most discrete features one tile holds.</summary>
    public int MaxFeaturesPerTile { get; }

    public async Task<TilePayload> GetVectorTileAsync(TileRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        var address = request.Address;
        var layers = new List<string>();
        var discrete = new List<string>();
        var aggregate = new List<string>();
        foreach (var layerId in request.LayerSet.LayerIds)
        {
            var tier = _lodPolicy.Decide(layerId, address.Z);
            if (tier == TileRenderTier.Hidden) continue;

            layers.Add(layerId);
            (tier == TileRenderTier.Aggregate ? aggregate : discrete).Add(layerId);
        }

        if (layers.Count == 0) return TilePayload.Empty(TileFormats.MapboxVectorTileContentType);

        var content = await ReadTileAsync(address, layers, discrete, aggregate).ConfigureAwait(false);
        return content is { Length: > 0 }
            ? new TilePayload(content, TileFormats.MapboxVectorTileContentType)
            : TilePayload.Empty(TileFormats.MapboxVectorTileContentType);
    }

    private async Task<byte[]?> ReadTileAsync(TileAddress address, List<string> layers, List<string> discrete, List<string> aggregate)
    {
        using var timeout = new CancellationTokenSource(QueryTimeout, _timeProvider);
        var db = new GeoAssetsDbContext(_dbOptions, _tenantContext);
        await using (db.ConfigureAwait(false))
        {
            // The transaction exists for TenantTransactionInterceptor's transaction-local stamp; this is a single read.
            var transaction = await db.Database.BeginTransactionAsync(timeout.Token).ConfigureAwait(false);
            await using (transaction.ConfigureAwait(false))
            {
                var command = new NpgsqlCommand(
                    TileSql, (NpgsqlConnection)db.Database.GetDbConnection(), (NpgsqlTransaction)transaction.GetDbTransaction());
                await using (command.ConfigureAwait(false))
                {
                    AddParameters(command, address, layers, discrete, aggregate);
                    var result = await command.ExecuteScalarAsync(timeout.Token).ConfigureAwait(false);
                    await transaction.CommitAsync(timeout.Token).ConfigureAwait(false);
                    return result as byte[];
                }
            }
        }
    }

    private void AddParameters(NpgsqlCommand command, TileAddress address, List<string> layers, List<string> discrete, List<string> aggregate)
    {
        var tileWidth = address.Envelope3857.Width;
        var organizationId = _tenantContext.CurrentOrganizationId;
        Guid[] organizations = organizationId == Guid.Empty ? [Guid.Empty] : [organizationId, Guid.Empty];

        var parameters = command.Parameters;
        parameters.Add(new NpgsqlParameter("z", NpgsqlDbType.Integer) { Value = address.Z });
        parameters.Add(new NpgsqlParameter("x", NpgsqlDbType.Integer) { Value = address.X });
        parameters.Add(new NpgsqlParameter("y", NpgsqlDbType.Integer) { Value = address.Y });
        parameters.Add(new NpgsqlParameter("margin", NpgsqlDbType.Double) { Value = (double)Buffer / Extent });
        parameters.Add(new NpgsqlParameter("extent", NpgsqlDbType.Integer) { Value = Extent });
        parameters.Add(new NpgsqlParameter("buffer", NpgsqlDbType.Integer) { Value = Buffer });
        parameters.Add(new NpgsqlParameter("tolerance", NpgsqlDbType.Double) { Value = tileWidth / Extent });
        parameters.Add(new NpgsqlParameter("cell", NpgsqlDbType.Double) { Value = tileWidth / AggregateGridSize });
        parameters.Add(new NpgsqlParameter("budget", NpgsqlDbType.Integer) { Value = MaxFeaturesPerTile });
        parameters.Add(new NpgsqlParameter("organizations", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = organizations });
        parameters.Add(TextArray("layers", layers));
        parameters.Add(TextArray("discrete", discrete));
        parameters.Add(TextArray("aggregate", aggregate));
        parameters.Add(TextArray("reserved", ReservedProperties));
    }

    private static NpgsqlParameter TextArray(string name, IReadOnlyCollection<string> values) =>
        new(name, NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = values.ToArray() };
}
