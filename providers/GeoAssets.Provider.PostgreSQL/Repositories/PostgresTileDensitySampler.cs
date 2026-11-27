using System.Globalization;
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
/// The PostGIS <see cref="ITileDensitySampler"/> of the LOD calibration (ADR-002 D6, XD01-439), over the same
/// GiST-indexed <c>geom_3857</c> column and the same tenant scope as <see cref="PostgresVectorTileSource"/>.
///
/// <para><b>Seeds.</b> <see cref="SeedSql"/>: the centroids (EPSG:4326) of at most <c>maxSeeds</c> of the type's
/// features from a <c>TABLESAMPLE SYSTEM</c> block sample of <c>geo_entity</c> (fixed <c>REPEATABLE</c> seed, so a rerun
/// on unchanged data picks the same features) sized from the table's planner row estimate to read about
/// <see cref="SeedOversampling"/> × <c>maxSeeds</c> rows; when the sample holds fewer of the type's features (a rare
/// type), topped up with the first others (not already sampled) an index scan finds — bounded by <c>maxSeeds</c> either
/// way, each feature at most once.</para>
///
/// <para><b>Measuring.</b> <see cref="MeasureSql"/>: one statement per zoom for all its tiles — for each tile, the
/// type's features whose <c>geom_3857</c> intersects <c>ST_TileEnvelope</c> with the tile source's margin
/// (<see cref="PostgresVectorTileSource.Buffer"/> / <see cref="PostgresVectorTileSource.Extent"/>), at most
/// <c>maxFeaturesPerTile</c> of them read, counted with the sum of their <c>ST_NPoints</c>. The asset type's own zoom
/// range is the caller's to apply (the calibrator measures only the zooms a type is drawn at).</para>
///
/// <para><b>Scope.</b> As the tile source: a transaction stamped by <see cref="TenantTransactionInterceptor"/> for the
/// Row-Level Security policy, and the SQL keeps the caller's organization's rows and the shared (<see cref="Guid.Empty"/>)
/// ones — the shared ones alone without an organization. Never another organization's.</para>
///
/// <para><b>Bounds.</b> Each statement runs under <c>statement_timeout</c> = <see cref="StatementTimeout"/> (set for its
/// transaction only) and the same client-side timeout — past it, <see cref="TimeoutException"/>; a cancelled token throws <see cref="OperationCanceledException"/>
/// before a read starts (a started read is not handed it, as the tile source's: an abort mid-handshake can spoil a pooled
/// connection).</para>
/// </summary>
public sealed class PostgresTileDensitySampler : ITileDensitySampler
{
    /// <summary>How many rows the block sample aims to read per seed wanted (a type with at least 1/16 of the table fills its seeds from it).</summary>
    public const int SeedOversampling = 16;

    /// <summary>The <c>REPEATABLE</c> seed of the block sample.</summary>
    public const int SampleRepeatSeed = 439;

    /// <summary>The longest one calibration statement may run.</summary>
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

    private readonly DbContextOptions<GeoAssetsDbContext> _dbOptions;
    private readonly ITenantContext _tenantContext;
    private readonly TimeProvider _timeProvider;

    /// <param name="dbOptions">Options for the per-call contexts. Must not enable a retrying execution strategy: each read runs in an explicit transaction.</param>
    /// <param name="tenantContext">The request's tenant; <c>null</c> = none (the shared features only).</param>
    /// <param name="timeProvider">The statement timeout's clock.</param>
    public PostgresTileDensitySampler(DbContextOptions<GeoAssetsDbContext> dbOptions, ITenantContext? tenantContext, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(dbOptions);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _tenantContext = tenantContext ?? NullTenantContext.Instance;
        _dbOptions = new DbContextOptionsBuilder<GeoAssetsDbContext>(dbOptions)
            .AddInterceptors(new TenantTransactionInterceptor(_tenantContext))
            .Options;
        _timeProvider = timeProvider;
    }

    /// <summary>The block sample's percentage of <c>geo_entity</c>'s pages for <paramref name="maxSeeds"/> seeds out of <paramref name="estimatedRows"/> rows (100 when the estimate is unknown).</summary>
    public static double SamplePercent(int maxSeeds, double estimatedRows) =>
        estimatedRows <= 0 ? 100d : Math.Clamp(100d * maxSeeds * SeedOversampling / estimatedRows, 0.0001, 100d);

    public Task<IReadOnlyList<TileDensitySeed>> SampleSeedsAsync(string assetTypeId, int maxSeeds, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetTypeId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSeeds);
        if (ct.IsCancellationRequested) return Task.FromCanceled<IReadOnlyList<TileDensitySeed>>(ct);

        return ReadAsync(
            async (connection, transaction, token) =>
            {
                double estimate;
                var rows = new NpgsqlCommand(RowEstimateSql, connection, transaction);
                await using (rows.ConfigureAwait(false))
                    estimate = await rows.ExecuteScalarAsync(token).ConfigureAwait(false) is double value ? value : -1;

                var command = new NpgsqlCommand(SeedSql, connection, transaction);
                await using (command.ConfigureAwait(false))
                {
                    var parameters = command.Parameters;
                    parameters.Add(new NpgsqlParameter("percent", NpgsqlDbType.Real) { Value = (float)SamplePercent(maxSeeds, estimate) });
                    parameters.Add(new NpgsqlParameter("repeat", NpgsqlDbType.Double) { Value = (double)SampleRepeatSeed });
                    parameters.Add(new NpgsqlParameter("type", NpgsqlDbType.Text) { Value = assetTypeId });
                    parameters.Add(Organizations());
                    parameters.Add(new NpgsqlParameter("seeds", NpgsqlDbType.Integer) { Value = maxSeeds });

                    var seeds = new List<TileDensitySeed>(maxSeeds);
                    var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
                    await using (reader.ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                        {
                            if (await reader.IsDBNullAsync(0, token).ConfigureAwait(false) || await reader.IsDBNullAsync(1, token).ConfigureAwait(false)) continue;
                            seeds.Add(new TileDensitySeed(reader.GetDouble(0), reader.GetDouble(1)));
                        }
                    }

                    return (IReadOnlyList<TileDensitySeed>)seeds;
                }
            });
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
        ct.ThrowIfCancellationRequested();

        return await ReadAsync(
            async (connection, transaction, token) =>
            {
                var command = new NpgsqlCommand(MeasureSql, connection, transaction);
                await using (command.ConfigureAwait(false))
                {
                    var parameters = command.Parameters;
                    parameters.Add(new NpgsqlParameter("xs", NpgsqlDbType.Array | NpgsqlDbType.Integer) { Value = tiles.Select(t => t.X).ToArray() });
                    parameters.Add(new NpgsqlParameter("ys", NpgsqlDbType.Array | NpgsqlDbType.Integer) { Value = tiles.Select(t => t.Y).ToArray() });
                    parameters.Add(new NpgsqlParameter("z", NpgsqlDbType.Integer) { Value = zoom });
                    parameters.Add(new NpgsqlParameter("margin", NpgsqlDbType.Double) { Value = (double)PostgresVectorTileSource.Buffer / PostgresVectorTileSource.Extent });
                    parameters.Add(new NpgsqlParameter("type", NpgsqlDbType.Text) { Value = assetTypeId });
                    parameters.Add(Organizations());
                    parameters.Add(new NpgsqlParameter("cap", NpgsqlDbType.Integer) { Value = maxFeaturesPerTile });

                    var samples = new List<TileDensitySample>(tiles.Count);
                    var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
                    await using (reader.ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                        {
                            var features = reader.GetInt32(0);
                            samples.Add(new TileDensitySample(tiles[samples.Count], features, reader.GetInt64(1), features >= maxFeaturesPerTile));
                        }
                    }

                    return (IReadOnlyList<TileDensitySample>)samples;
                }
            }).ConfigureAwait(false);
    }

    /// <exception cref="TimeoutException">A statement ran past <see cref="StatementTimeout"/>.</exception>
    private async Task<T> ReadAsync<T>(Func<NpgsqlConnection, NpgsqlTransaction, CancellationToken, Task<T>> read)
    {
        using var timeout = new CancellationTokenSource(StatementTimeout, _timeProvider);
        try
        {
            return await ReadAsync(read, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (timeout.IsCancellationRequested)
        {
            throw Timeout(ex);
        }
        catch (PostgresException ex) when (string.Equals(ex.SqlState, PostgresErrorCodes.QueryCanceled, StringComparison.Ordinal))
        {
            throw Timeout(ex);
        }
    }

    private static TimeoutException Timeout(Exception inner) =>
        new(string.Create(CultureInfo.InvariantCulture, $"A LOD calibration statement ran past its {StatementTimeout.TotalSeconds:0} s timeout."), inner);

    private async Task<T> ReadAsync<T>(Func<NpgsqlConnection, NpgsqlTransaction, CancellationToken, Task<T>> read, CancellationToken timeout)
    {
        var db = new GeoAssetsDbContext(_dbOptions, _tenantContext);
        await using (db.ConfigureAwait(false))
        {
            // The transaction exists for TenantTransactionInterceptor's transaction-local stamp and the local statement_timeout.
            var transaction = await db.Database.BeginTransactionAsync(timeout).ConfigureAwait(false);
            await using (transaction.ConfigureAwait(false))
            {
                var connection = (NpgsqlConnection)db.Database.GetDbConnection();
                var npgsqlTransaction = (NpgsqlTransaction)transaction.GetDbTransaction();

                var limit = new NpgsqlCommand("SELECT set_config('statement_timeout', @timeout, true)", connection, npgsqlTransaction);
                await using (limit.ConfigureAwait(false))
                {
                    limit.Parameters.Add(new NpgsqlParameter("timeout", NpgsqlDbType.Text)
                    {
                        Value = ((long)StatementTimeout.TotalMilliseconds).ToString(CultureInfo.InvariantCulture),
                    });
                    await limit.ExecuteScalarAsync(timeout).ConfigureAwait(false);
                }

                var result = await read(connection, npgsqlTransaction, timeout).ConfigureAwait(false);
                await transaction.CommitAsync(timeout).ConfigureAwait(false);
                return result;
            }
        }
    }

    private NpgsqlParameter Organizations()
    {
        var organizationId = _tenantContext.CurrentOrganizationId;
        Guid[] organizations = organizationId == Guid.Empty ? [Guid.Empty] : [organizationId, Guid.Empty];
        return new NpgsqlParameter("organizations", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = organizations };
    }
}
