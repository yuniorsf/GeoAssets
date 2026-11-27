using System.Globalization;
using GeoAssets.Core.Rendering;
using Npgsql;
using NpgsqlTypes;

namespace GeoAssets.Bench.Render;

/// <summary>
/// The kernel's LOD calibration reads (XD01-439), reproduced for an organization-less caller: <see cref="SeedSql"/>,
/// <see cref="MeasureSql"/> and their parameters are copies of <c>PostgresTileDensitySampler</c>'s — the tool cannot
/// reference the provider (Invariant 1) — kept equal by <c>tests/GeoAssets.Bench.Render.Tests</c>, which compares what both
/// answer. The calibration itself is <see cref="LodCalibrator"/>'s, from GeoAssets.Core, as on the kernel.
/// </summary>
internal sealed class BenchTileDensitySampler(NpgsqlDataSource dataSource) : ITileDensitySampler
{
    /// <summary><c>PostgresTileDensitySampler.SeedOversampling</c>.</summary>
    public const int SeedOversampling = 16;

    /// <summary><c>PostgresTileDensitySampler.SampleRepeatSeed</c>.</summary>
    public const int SampleRepeatSeed = 439;

    /// <summary><c>PostgresTileDensitySampler.StatementTimeout</c>.</summary>
    public static readonly TimeSpan StatementTimeout = TimeSpan.FromSeconds(30);

    internal const string RowEstimateSql = "SELECT reltuples::float8 FROM pg_class WHERE oid = 'geo_entity'::regclass";

    internal const string SeedSql = """
        WITH sampled AS MATERIALIZED (
            SELECT e."Id" AS id, ST_X(c.point) AS lon, ST_Y(c.point) AS lat
            FROM geo_entity e TABLESAMPLE SYSTEM (@percent) REPEATABLE (@repeat)
            CROSS JOIN LATERAL (SELECT ST_Centroid(e.geom) AS point) AS c
            WHERE e."AssetTypeId" = @type
              AND e."OrganizationId" = ANY(@organizations)
              AND e.geom_3857 IS NOT NULL AND NOT ST_IsEmpty(e.geom_3857)
            LIMIT @seeds
        ),
        topped AS (
            SELECT ST_X(c.point) AS lon, ST_Y(c.point) AS lat
            FROM geo_entity e
            CROSS JOIN LATERAL (SELECT ST_Centroid(e.geom) AS point) AS c
            WHERE (SELECT count(*) FROM sampled) < @seeds
              AND e."Id" NOT IN (SELECT id FROM sampled)
              AND e."AssetTypeId" = @type
              AND e."OrganizationId" = ANY(@organizations)
              AND e.geom_3857 IS NOT NULL AND NOT ST_IsEmpty(e.geom_3857)
            LIMIT @seeds
        )
        SELECT u.lon, u.lat
        FROM (SELECT 0 AS src, lon, lat FROM sampled UNION ALL SELECT 1 AS src, lon, lat FROM topped) AS u
        ORDER BY u.src
        LIMIT @seeds
        """;

    internal const string MeasureSql = """
        SELECT d.features, d.vertices
        FROM unnest(@xs, @ys) WITH ORDINALITY AS t(x, y, ord)
        CROSS JOIN LATERAL (
            SELECT count(*)::int AS features, coalesce(sum(ST_NPoints(s.geom_3857)), 0)::bigint AS vertices
            FROM (
                SELECT e.geom_3857
                FROM geo_entity e
                WHERE e."AssetTypeId" = @type
                  AND e."OrganizationId" = ANY(@organizations)
                  AND e.geom_3857 && ST_TileEnvelope(@z, t.x, t.y, margin => @margin)
                LIMIT @cap
            ) AS s
        ) AS d
        ORDER BY t.ord
        """;

    /// <summary><c>PostgresTileDensitySampler.SamplePercent</c>.</summary>
    public static double SamplePercent(int maxSeeds, double estimatedRows) =>
        estimatedRows <= 0 ? 100d : Math.Clamp(100d * maxSeeds * SeedOversampling / estimatedRows, 0.0001, 100d);

    public async Task<IReadOnlyList<TileDensitySeed>> SampleSeedsAsync(string assetTypeId, int maxSeeds, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetTypeId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSeeds);

        var (connection, transaction) = await BeginAsync(ct).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        await using (transaction.ConfigureAwait(false))
        {
            double estimate;
            var rows = new NpgsqlCommand(RowEstimateSql, connection, transaction);
            await using (rows.ConfigureAwait(false))
                estimate = await rows.ExecuteScalarAsync(ct).ConfigureAwait(false) is double value ? value : -1;

            var seeds = new List<TileDensitySeed>(maxSeeds);
            var command = new NpgsqlCommand(SeedSql, connection, transaction);
            await using (command.ConfigureAwait(false))
            {
                var parameters = command.Parameters;
                parameters.Add(new NpgsqlParameter("percent", NpgsqlDbType.Real) { Value = (float)SamplePercent(maxSeeds, estimate) });
                parameters.Add(new NpgsqlParameter("repeat", NpgsqlDbType.Double) { Value = (double)SampleRepeatSeed });
                parameters.Add(new NpgsqlParameter("type", NpgsqlDbType.Text) { Value = assetTypeId });
                parameters.Add(Organizations());
                parameters.Add(new NpgsqlParameter("seeds", NpgsqlDbType.Integer) { Value = maxSeeds });

                var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
                await using (reader.ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(ct).ConfigureAwait(false))
                    {
                        if (await reader.IsDBNullAsync(0, ct).ConfigureAwait(false) || await reader.IsDBNullAsync(1, ct).ConfigureAwait(false)) continue;
                        seeds.Add(new TileDensitySeed(reader.GetDouble(0), reader.GetDouble(1)));
                    }
                }
            }

            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return seeds;
        }
    }

    public async Task<IReadOnlyList<TileDensitySample>> MeasureAsync(
        string assetTypeId, IReadOnlyList<TileAddress> tiles, int maxFeaturesPerTile, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetTypeId);
        ArgumentNullException.ThrowIfNull(tiles);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxFeaturesPerTile);
        if (tiles.Count == 0) return [];
        var zoom = tiles[0].Z;
        if (tiles.Any(t => t.Z != zoom)) throw new ArgumentException("Every tile must be of one zoom.", nameof(tiles));

        var (connection, transaction) = await BeginAsync(ct).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        await using (transaction.ConfigureAwait(false))
        {
            var samples = new List<TileDensitySample>(tiles.Count);
            var command = new NpgsqlCommand(MeasureSql, connection, transaction);
            await using (command.ConfigureAwait(false))
            {
                var parameters = command.Parameters;
                parameters.Add(new NpgsqlParameter("xs", NpgsqlDbType.Array | NpgsqlDbType.Integer) { Value = tiles.Select(t => t.X).ToArray() });
                parameters.Add(new NpgsqlParameter("ys", NpgsqlDbType.Array | NpgsqlDbType.Integer) { Value = tiles.Select(t => t.Y).ToArray() });
                parameters.Add(new NpgsqlParameter("z", NpgsqlDbType.Integer) { Value = zoom });
                parameters.Add(new NpgsqlParameter("margin", NpgsqlDbType.Double) { Value = (double)TileEndpointPipeline.Buffer / TileEndpointPipeline.Extent });
                parameters.Add(new NpgsqlParameter("type", NpgsqlDbType.Text) { Value = assetTypeId });
                parameters.Add(Organizations());
                parameters.Add(new NpgsqlParameter("cap", NpgsqlDbType.Integer) { Value = maxFeaturesPerTile });

                var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
                await using (reader.ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(ct).ConfigureAwait(false))
                    {
                        var features = reader.GetInt32(0);
                        samples.Add(new TileDensitySample(tiles[samples.Count], features, reader.GetInt64(1), features >= maxFeaturesPerTile));
                    }
                }
            }

            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return samples;
        }
    }

    private async Task<(NpgsqlConnection Connection, NpgsqlTransaction Transaction)> BeginAsync(CancellationToken ct)
    {
        var connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        var limit = new NpgsqlCommand("SELECT set_config('statement_timeout', @timeout, true)", connection, transaction);
        await using (limit.ConfigureAwait(false))
        {
            limit.Parameters.Add(new NpgsqlParameter("timeout", NpgsqlDbType.Text)
            {
                Value = ((long)StatementTimeout.TotalMilliseconds).ToString(CultureInfo.InvariantCulture),
            });
            await limit.ExecuteScalarAsync(ct).ConfigureAwait(false);
        }

        return (connection, transaction);
    }

    private static NpgsqlParameter Organizations() =>
        new("organizations", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = new[] { Guid.Empty } };
}
