using System.Globalization;
using FluentAssertions;
using GeoAssets.Core.Rendering;
using GeoAssets.Provider.PostgreSQL.Data;
using GeoAssets.Provider.PostgreSQL.Repositories;
using GeoAssets.Provider.PostgreSQL.Tests.TestDoubles;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace GeoAssets.Provider.PostgreSQL.Tests;

/// <summary>
/// The <c>AddGeoEntityGeom3857Centroid</c> migration (XD01-457) against real PostGIS: the stored generated
/// <c>geom_3857_centroid</c> (the centroid of <c>geom_3857</c>, bit for bit), its GiST index and the aggregate filter's
/// access path, its recomputation when <c>geom</c> changes, the backfill of rows written before it and back down; and
/// <see cref="PostgresVectorTileSource"/>'s aggregate tier over it, which answers what the per-row
/// <c>ST_Centroid(geom_3857)</c> of XD01-432/XD01-434 answered.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class GeoEntityGeom3857CentroidMigrationTests(PostgresContainerFixture fixture)
{
    private const string PreviousMigration = "20261126234247_AddGeoEntityTopologyIndex";

    /// <summary>The aggregate cells as the tile source computed them before XD01-457: a centroid per row, per request.</summary>
    private const string CentroidPerRowCells = """
        WITH tile AS (
            SELECT t.envelope, ST_XMin(t.envelope) AS xmin, ST_YMin(t.envelope) AS ymin,
                   ST_XMax(t.envelope) AS xmax, ST_YMax(t.envelope) AS ymax
            FROM (SELECT ST_TileEnvelope(@z, @x, @y) AS envelope) AS t
        ),
        cells AS (
            SELECT count(*)::int AS count, avg(ST_X(p.point)) AS x, avg(ST_Y(p.point)) AS y
            FROM geo_entity e
            CROSS JOIN tile
            CROSS JOIN LATERAL (SELECT ST_Centroid(e.geom_3857) AS point) AS p
            WHERE e."AssetTypeId" = @type
              AND e.geom_3857 && tile.envelope
              AND ST_X(p.point) >= tile.xmin AND ST_X(p.point) < tile.xmax
              AND ST_Y(p.point) >= tile.ymin AND ST_Y(p.point) < tile.ymax
            GROUP BY floor((ST_X(p.point) - tile.xmin) / @cell), floor((ST_Y(p.point) - tile.ymin) / @cell)
        )
        SELECT c.count, ST_X(g.geom)::int, ST_Y(g.geom)::int
        FROM cells c
        CROSS JOIN tile
        CROSS JOIN LATERAL (SELECT ST_AsMVTGeom(ST_SetSRID(ST_MakePoint(c.x, c.y), 3857), tile.envelope, 4096, 64, true) AS geom) AS g
        WHERE g.geom IS NOT NULL
        """;

    private static string NewId(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..30];

    private static async Task<NpgsqlConnection> OpenAsync(string connectionString)
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        return connection;
    }

    private static async Task<object?> ScalarAsync(NpgsqlCommand command, params (string Name, object Value)[] parameters)
    {
        await using (command)
        {
            foreach (var (name, value) in parameters)
                command.Parameters.AddWithValue(name, value);
            return await command.ExecuteScalarAsync();
        }
    }

    /// <summary>Inserts a shared row of <paramref name="type"/> whose geometry is <paramref name="wkt"/> (SRID 4326; <c>null</c> = none).</summary>
    private static Task<object?> InsertAsync(NpgsqlConnection connection, string id, string? wkt, string type = "xd457") =>
        ScalarAsync(new NpgsqlCommand("""
            INSERT INTO geo_entity ("Id", "Name", "AssetTypeId", "OrganizationId", geom, custom_attributes, topology)
            VALUES (@id, @id, @type, '00000000-0000-0000-0000-000000000000', ST_GeomFromText(@wkt::text, 4326), '{}', '[]')
            """, connection),
            ("id", id),
            ("type", type),
            ("wkt", (object?)wkt ?? DBNull.Value));

    private static Task<object?> DeleteAsync(NpgsqlConnection connection, params string[] ids) =>
        ScalarAsync(new NpgsqlCommand("""DELETE FROM geo_entity WHERE "Id" = ANY(@ids)""", connection), ("ids", ids));

    /// <summary>Whether <paramref name="id"/>'s stored centroid is <c>ST_Centroid(geom_3857)</c> exactly (both coordinates).</summary>
    private static async Task<bool> CentroidIsExactAsync(NpgsqlConnection connection, string id) =>
        (bool)(await ScalarAsync(new NpgsqlCommand("""
            SELECT ST_X(geom_3857_centroid) = ST_X(ST_Centroid(geom_3857)) AND ST_Y(geom_3857_centroid) = ST_Y(ST_Centroid(geom_3857))
            FROM geo_entity WHERE "Id" = @id
            """, connection),
            ("id", id)))!;

    // ── Fresh database ────────────────────────────────────────────────────────

    [Fact]
    public async Task TheCentroid_IsAStoredGeneratedPoint_WithAGistIndex()
    {
        await using var connection = await OpenAsync(fixture.ConnectionString);

        (await ScalarAsync(new NpgsqlCommand("""
            SELECT format_type(atttypid, atttypmod) || ' ' || attgenerated::text FROM pg_attribute
            WHERE attrelid = 'geo_entity'::regclass AND attname = 'geom_3857_centroid'
            """, connection))).Should().Be("geometry(Point,3857) s", "a stored ('s') generated column");
        ((string)(await ScalarAsync(new NpgsqlCommand("""SELECT indexdef FROM pg_indexes WHERE indexname = 'IX_geo_entity_geom_3857_centroid'""", connection))
            ?? string.Empty)).Should().Contain("USING gist (geom_3857_centroid)");
    }

    [Fact]
    public async Task AWrite_StoresTheCentroidOfItsWebMercatorCopy_AndAnUpdateRecomputesIt()
    {
        await using var connection = await OpenAsync(fixture.ConnectionString);
        var concave = NewId("concave");
        var line = NewId("line");
        await InsertAsync(connection, concave, "POLYGON((10 10, 13 10, 13 11, 11 11, 11 12, 13 12, 13 13, 10 13, 10 10))");
        await InsertAsync(connection, line, "LINESTRING(-38.5 -12.9, -38.4 -12.8, -38.2 -12.85)");
        try
        {
            (await CentroidIsExactAsync(connection, concave)).Should().BeTrue();
            (await CentroidIsExactAsync(connection, line)).Should().BeTrue();

            await ScalarAsync(new NpgsqlCommand("""UPDATE geo_entity SET geom = ST_SetSRID(ST_MakePoint(11, 21), 4326) WHERE "Id" = @id""", connection), ("id", concave));

            (await CentroidIsExactAsync(connection, concave)).Should().BeTrue();
            var expected = WebMercator.Project(11, 21);
            ((double)(await ScalarAsync(new NpgsqlCommand("""SELECT ST_X(geom_3857_centroid) FROM geo_entity WHERE "Id" = @id""", connection), ("id", concave)))!)
                .Should().BeApproximately(expected.X, 0.01, "the centroid follows geom");
            ((double)(await ScalarAsync(new NpgsqlCommand("""SELECT ST_Y(geom_3857_centroid) FROM geo_entity WHERE "Id" = @id""", connection), ("id", concave)))!)
                .Should().BeApproximately(expected.Y, 0.01);
        }
        finally
        {
            await DeleteAsync(connection, concave, line);
        }
    }

    [Fact]
    public async Task NoGeometry_HasNoCentroid_AndOneBeyondWebMercator_AnEmptyOne()
    {
        await using var connection = await OpenAsync(fixture.ConnectionString);
        var none = NewId("none");
        var pole = NewId("pole");
        await InsertAsync(connection, none, null);
        await InsertAsync(connection, pole, "POINT(10 89.9)");
        try
        {
            (await ScalarAsync(new NpgsqlCommand("""SELECT geom_3857_centroid IS NULL FROM geo_entity WHERE "Id" = @id""", connection), ("id", none))).Should().Be(true);
            (await ScalarAsync(new NpgsqlCommand("""SELECT ST_IsEmpty(geom_3857_centroid) FROM geo_entity WHERE "Id" = @id""", connection), ("id", pole))).Should().Be(true);
        }
        finally
        {
            await DeleteAsync(connection, none, pole);
        }
    }

    [Fact]
    public async Task TheAggregateFilter_ReadsThroughTheCentroidGistIndex()
    {
        await using var connection = await OpenAsync(fixture.ConnectionString);
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var noSeqScan = new NpgsqlCommand("SET LOCAL enable_seqscan = off", connection, transaction))
            await noSeqScan.ExecuteNonQueryAsync();

        await using var explain = new NpgsqlCommand(
            """
            EXPLAIN SELECT count(*) FROM geo_entity e
            WHERE e."OrganizationId" = ANY(@organizations) AND e.geom_3857_centroid && ST_TileEnvelope(8, 95, 144)
            """,
            connection,
            transaction);
        explain.Parameters.AddWithValue("organizations", new[] { Guid.NewGuid(), Guid.Empty });
        var plan = new List<string>();
        await using (var reader = await explain.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
                plan.Add(reader.GetString(0));
        }

        string.Join('\n', plan).Should().Contain("IX_geo_entity_geom_3857_centroid");
    }

    // ── The aggregate tier over it ────────────────────────────────────────────

    [Fact]
    public async Task AnAggregateTile_CountsAndPlacesItsCells_AsTheCentroidPerRowDid()
    {
        var type = Guid.NewGuid().ToString("D");
        var tile = TileAddress.Containing(-46.6, -23.5, 12);
        var box = tile.Envelope4326;
        var width = box.MaxLon - box.MinLon;
        var height = box.MaxLat - box.MinLat;
        string At(double fx, double fy) => string.Create(CultureInfo.InvariantCulture, $"{box.MinLon + (fx * width)} {box.MinLat + (fy * height)}");

        var ids = new List<string>();
        await using var connection = await OpenAsync(fixture.ConnectionString);
        try
        {
            for (var i = 0; i < 24; i++)
            {
                // Small squares, several per cell in places (a 64 x 64 grid: a cell is 1/64 of the tile).
                var (fx, fy) = (0.05 + (0.006 * (i % 6)), 0.05 + (0.007 * (i / 6)));
                var id = NewId("sq");
                ids.Add(id);
                await InsertAsync(connection, id, $"POLYGON(({At(fx, fy)}, {At(fx + 0.004, fy)}, {At(fx + 0.004, fy + 0.004)}, {At(fx, fy + 0.004)}, {At(fx, fy)}))", type);
            }

            // A concave district whose centroid lies outside it, a line, a point, one that straddles the east edge with
            // its centroid in the next tile (counted there, not here), and one wholly elsewhere.
            (string Prefix, string Wkt)[] others =
            [
                ("c", $"POLYGON(({At(0.5, 0.5)}, {At(0.8, 0.5)}, {At(0.8, 0.55)}, {At(0.55, 0.55)}, {At(0.55, 0.75)}, {At(0.8, 0.75)}, {At(0.8, 0.8)}, {At(0.5, 0.8)}, {At(0.5, 0.5)}))"),
                ("l", $"LINESTRING({At(0.3, 0.6)}, {At(0.35, 0.7)}, {At(0.42, 0.66)})"),
                ("p", $"POINT({At(0.9, 0.2)})"),
                ("e", $"POLYGON(({At(0.95, 0.4)}, {At(1.3, 0.4)}, {At(1.3, 0.45)}, {At(0.95, 0.45)}, {At(0.95, 0.4)}))"),
                ("far", $"POINT({At(3.5, 3.5)})"),
            ];
            foreach (var (prefix, wkt) in others)
            {
                var id = NewId(prefix);
                ids.Add(id);
                await InsertAsync(connection, id, wkt, type);
            }

            var expected = new List<(long Count, int X, int Y)>();
            await using (var command = new NpgsqlCommand(CentroidPerRowCells, connection))
            {
                command.Parameters.AddWithValue("z", tile.Z);
                command.Parameters.AddWithValue("x", tile.X);
                command.Parameters.AddWithValue("y", tile.Y);
                command.Parameters.AddWithValue("type", type);
                command.Parameters.AddWithValue("cell", tile.Envelope3857.Width / PostgresVectorTileSource.AggregateGridSize);
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    expected.Add((reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2)));
            }

            var policy = new LodPolicy(LodThresholds.VectorOnly, new Dictionary<string, LodThresholds>(StringComparer.Ordinal) { [type] = new(20, 20) });
            var source = new PostgresVectorTileSource(fixture.CreateOptions(), tenantContext: null, policy, TimeProvider.System);
            var payload = await source.GetVectorTileAsync(new TileRequest(new TileLayerSet("test", [type]), tile), CancellationToken.None);
            var actual = MvtTile.Decode(payload.Content)[type].Features
                .Select(f => (Count: Convert.ToInt64(f.Properties["count"], CultureInfo.InvariantCulture), f.Parts[0][0].X, f.Parts[0][0].Y))
                .ToList();

            expected.Sum(c => c.Count).Should().Be(27, "the 24 squares, the concave district, the line and the point — not the straddling one, nor the far one");
            expected.Should().Contain(c => c.Count > 1, "several squares share a cell");
            actual.Should().BeEquivalentTo(expected, "the stored centroid is the per-row ST_Centroid(geom_3857), bit for bit");
        }
        finally
        {
            await DeleteAsync(connection, [.. ids]);
        }
    }

    // ── A database with rows from before ──────────────────────────────────────

    [Fact]
    public async Task OnADatabaseWithRows_BackfillsTheCentroid_AndDownDropsIt()
    {
        var database = $"xd457_{Guid.NewGuid():N}";
        var create = "CREATE DATABASE " + database;
        await AdministerAsync(create);

        var connectionString = new NpgsqlConnectionStringBuilder(fixture.ConnectionString) { Database = database, Pooling = false }.ConnectionString;
        try
        {
            var options = new DbContextOptionsBuilder<GeoAssetsDbContext>()
                .UseNpgsql(connectionString, npgsql => npgsql.UseNetTopologySuite())
                .Options;
            await using var db = new GeoAssetsDbContext(options);
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync(PreviousMigration);

            await using (var before = await OpenAsync(connectionString))
            {
                await InsertAsync(before, "district", "POLYGON((10 10, 13 10, 13 11, 11 11, 11 12, 13 12, 13 13, 10 13, 10 10))");
                await InsertAsync(before, "pole", "POINT(-38.5 -12.9)");
                await InsertAsync(before, "no-geometry", null);
            }

            await migrator.MigrateAsync();

            await using (var after = await OpenAsync(connectionString))
            {
                (await CentroidIsExactAsync(after, "district")).Should().BeTrue();
                (await CentroidIsExactAsync(after, "pole")).Should().BeTrue();
                (await ScalarAsync(new NpgsqlCommand("""SELECT geom_3857_centroid IS NULL FROM geo_entity WHERE "Id" = 'no-geometry'""", after))).Should().Be(true);
            }

            await migrator.MigrateAsync(PreviousMigration);

            await using (var down = await OpenAsync(connectionString))
            {
                (await ScalarAsync(new NpgsqlCommand("""
                    SELECT count(*) FROM pg_attribute WHERE attrelid = 'geo_entity'::regclass AND attname = 'geom_3857_centroid' AND NOT attisdropped
                    """, down))).Should().Be(0L);
                (await ScalarAsync(new NpgsqlCommand("""SELECT count(*) FROM pg_indexes WHERE indexname = 'IX_geo_entity_geom_3857_centroid'""", down))).Should().Be(0L);
                (await ScalarAsync(new NpgsqlCommand("SELECT count(*) FROM geo_entity", down))).Should().Be(3L);
            }
        }
        finally
        {
            var drop = "DROP DATABASE IF EXISTS " + database + " WITH (FORCE)";
            await AdministerAsync(drop);
        }
    }

    /// <summary>Runs <paramref name="sql"/> (no parameters: it names a database) on the collection's database.</summary>
    private async Task AdministerAsync(string sql)
    {
        await using var db = new GeoAssetsDbContext(fixture.CreateOptions());
        await db.Database.ExecuteSqlRawAsync(sql);
    }
}
