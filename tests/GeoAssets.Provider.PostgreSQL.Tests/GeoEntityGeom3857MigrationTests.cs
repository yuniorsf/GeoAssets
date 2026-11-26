using FluentAssertions;
using GeoAssets.Core.Rendering;
using GeoAssets.Provider.PostgreSQL.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace GeoAssets.Provider.PostgreSQL.Tests;

/// <summary>
/// The <c>AddGeoEntityGeom3857</c> migration (XD01-432) against real PostGIS: on the collection's freshly migrated
/// database (the column types, the stored generated <c>geom_3857</c>, its GiST index and the tile filter's access path,
/// the SRID constraint on writes), and on a database of its own holding rows written before it (normalized SRIDs,
/// <c>geom_3857</c> backfilled, and back down again).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class GeoEntityGeom3857MigrationTests(PostgresContainerFixture fixture)
{
    private const string PreviousMigration = "20261126185552_AddElectricRolesToSeededAssetTypes";

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

    /// <summary>Runs <paramref name="sql"/> (no parameters: it names a database) on the collection's database.</summary>
    private async Task AdministerAsync(string sql)
    {
        await using var db = new GeoAssetsDbContext(fixture.CreateOptions());
        await db.Database.ExecuteSqlRawAsync(sql);
    }

    /// <summary>Inserts a row whose geometry is <paramref name="wkt"/> in <paramref name="srid"/> (0 = none; <c>null</c> WKT = no geometry).</summary>
    private static Task<object?> InsertAsync(NpgsqlConnection connection, string id, string? wkt, int srid = 4326) =>
        ScalarAsync(
            new NpgsqlCommand(
                """
                INSERT INTO geo_entity ("Id", "Name", "AssetTypeId", "OrganizationId", geom, custom_attributes, topology)
                VALUES (@id, @id, 'xd432', '00000000-0000-0000-0000-000000000000', ST_GeomFromText(@wkt::text, @srid), '{}', '[]')
                """,
                connection),
            ("id", id),
            ("wkt", (object?)wkt ?? DBNull.Value),
            ("srid", srid));

    private static async Task<(double X, double Y)?> Geom3857Async(NpgsqlConnection connection, string id)
    {
        await using var command = new NpgsqlCommand(
            """SELECT ST_X(geom_3857), ST_Y(geom_3857) FROM geo_entity WHERE "Id" = @id AND NOT ST_IsEmpty(geom_3857)""", connection);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? (reader.GetDouble(0), reader.GetDouble(1)) : null;
    }

    private static Task<object?> DeleteAsync(NpgsqlConnection connection, params string[] ids) =>
        ScalarAsync(new NpgsqlCommand("""DELETE FROM geo_entity WHERE "Id" = ANY(@ids)""", connection), ("ids", ids));

    // ── Fresh database ────────────────────────────────────────────────────────

    [Fact]
    public async Task Geom_IsAnySrid4326Geometry_AndGeom3857_IsItsStoredGeneratedWebMercatorCopy()
    {
        await using var connection = await OpenAsync(fixture.ConnectionString);

        (await ScalarAsync(new NpgsqlCommand("""
            SELECT format_type(atttypid, atttypmod) FROM pg_attribute
            WHERE attrelid = 'geo_entity'::regclass AND attname = 'geom'
            """, connection))).Should().Be("geometry(Geometry,4326)");
        (await ScalarAsync(new NpgsqlCommand("""
            SELECT format_type(atttypid, atttypmod) || ' ' || attgenerated::text FROM pg_attribute
            WHERE attrelid = 'geo_entity'::regclass AND attname = 'geom_3857'
            """, connection))).Should().Be("geometry(Geometry,3857) s", "a stored ('s') generated column");
        ((string)(await ScalarAsync(new NpgsqlCommand("""SELECT indexdef FROM pg_indexes WHERE indexname = 'IX_geo_entity_geom_3857'""", connection))
            ?? string.Empty)).Should().Contain("USING gist (geom_3857)");
    }

    [Fact]
    public async Task AWriteWithoutAnSrid_TakesTheColumns_AndItsGeom3857_IsComputed()
    {
        await using var connection = await OpenAsync(fixture.ConnectionString);
        var id = NewId("srid0");
        await InsertAsync(connection, id, "POINT(-38.5 -12.9)", srid: 0);
        try
        {
            (await ScalarAsync(new NpgsqlCommand("""SELECT ST_SRID(geom) FROM geo_entity WHERE "Id" = @id""", connection), ("id", id))).Should().Be(4326);
            var expected = WebMercator.Project(-38.5, -12.9);
            var actual = (await Geom3857Async(connection, id))!.Value;
            actual.X.Should().BeApproximately(expected.X, 0.01);
            actual.Y.Should().BeApproximately(expected.Y, 0.01);
        }
        finally
        {
            await DeleteAsync(connection, id);
        }
    }

    [Fact]
    public async Task AWriteOfAnotherSrid_IsRefused()
    {
        await using var connection = await OpenAsync(fixture.ConnectionString);

        PostgresException? refused = null;
        try
        {
            await InsertAsync(connection, NewId("srid3857"), "POINT(1000 1000)", srid: 3857);
        }
        catch (PostgresException ex)
        {
            refused = ex;
        }

        refused.Should().NotBeNull();
        refused!.MessageText.Should().Contain("SRID");
    }

    [Fact]
    public async Task AnUpdatedGeometry_RecomputesGeom3857()
    {
        await using var connection = await OpenAsync(fixture.ConnectionString);
        var id = NewId("moved");
        await InsertAsync(connection, id, "POINT(10 20)");
        try
        {
            await ScalarAsync(new NpgsqlCommand("""UPDATE geo_entity SET geom = ST_SetSRID(ST_MakePoint(11, 21), 4326) WHERE "Id" = @id""", connection), ("id", id));

            var expected = WebMercator.Project(11, 21);
            (await Geom3857Async(connection, id))!.Value.X.Should().BeApproximately(expected.X, 0.01);
        }
        finally
        {
            await DeleteAsync(connection, id);
        }
    }

    [Fact]
    public async Task TheTileFilter_ReadsThroughTheGeom3857GistIndex()
    {
        await using var connection = await OpenAsync(fixture.ConnectionString);
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var noSeqScan = new NpgsqlCommand("SET LOCAL enable_seqscan = off", connection, transaction))
            await noSeqScan.ExecuteNonQueryAsync();

        await using var explain = new NpgsqlCommand(
            """
            EXPLAIN SELECT e."Id" FROM geo_entity e
            WHERE e."OrganizationId" = ANY(@organizations) AND e.geom_3857 && ST_TileEnvelope(14, 6439, 8784)
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

        string.Join('\n', plan).Should().Contain("IX_geo_entity_geom_3857");
    }

    // ── A database with rows from before ──────────────────────────────────────

    [Fact]
    public async Task OnADatabaseWithRows_NormalizesTheirSrid_BackfillsGeom3857_AndDownRestoresTheColumn()
    {
        var database = $"xd432_{Guid.NewGuid():N}";
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
                await InsertAsync(before, "no-srid", "POINT(-38.5 -12.9)", srid: 0);
                await InsertAsync(before, "wgs84-line", "LINESTRING(10 20, 11 21)");
                await InsertAsync(before, "mercator", "POINT(1000000 1000000)", srid: 3857);
                await InsertAsync(before, "pole", "POINT(10 89.9)");
                await InsertAsync(before, "no-geometry", null);
            }

            await migrator.MigrateAsync();

            await using (var after = await OpenAsync(connectionString))
            {
                (await ScalarAsync(new NpgsqlCommand("""SELECT count(*) FROM geo_entity WHERE geom IS NOT NULL AND ST_SRID(geom) <> 4326""", after))).Should().Be(0L);
                var expected = WebMercator.Project(-38.5, -12.9);
                (await Geom3857Async(after, "no-srid"))!.Value.X.Should().BeApproximately(expected.X, 0.01);
                (await Geom3857Async(after, "no-srid"))!.Value.Y.Should().BeApproximately(expected.Y, 0.01);
                (await ScalarAsync(new NpgsqlCommand("""SELECT ST_AsText(ST_SnapToGrid(geom_3857, 1)) FROM geo_entity WHERE "Id" = 'mercator'""", after)))
                    .Should().Be("POINT(1000000 1000000)", "a row of another SRID is reprojected to 4326, and back for geom_3857");
                (await ScalarAsync(new NpgsqlCommand("""SELECT ST_NPoints(geom_3857) FROM geo_entity WHERE "Id" = 'wgs84-line'""", after))).Should().Be(2);
                (await ScalarAsync(new NpgsqlCommand("""SELECT ST_IsEmpty(geom_3857) FROM geo_entity WHERE "Id" = 'pole'""", after)))
                    .Should().Be(true, "beyond Web Mercator's latitudes there is nothing to draw");
                (await ScalarAsync(new NpgsqlCommand("""SELECT geom_3857 IS NULL FROM geo_entity WHERE "Id" = 'no-geometry'""", after))).Should().Be(true);
            }

            await migrator.MigrateAsync(PreviousMigration);

            await using (var down = await OpenAsync(connectionString))
            {
                (await ScalarAsync(new NpgsqlCommand("""
                    SELECT count(*) FROM pg_attribute WHERE attrelid = 'geo_entity'::regclass AND attname = 'geom_3857' AND NOT attisdropped
                    """, down))).Should().Be(0L);
                (await ScalarAsync(new NpgsqlCommand("""
                    SELECT format_type(atttypid, atttypmod) FROM pg_attribute WHERE attrelid = 'geo_entity'::regclass AND attname = 'geom'
                    """, down))).Should().Be("geometry");
                (await ScalarAsync(new NpgsqlCommand("SELECT count(*) FROM geo_entity", down))).Should().Be(5L);
            }
        }
        finally
        {
            var drop = "DROP DATABASE IF EXISTS " + database + " WITH (FORCE)";
            await AdministerAsync(drop);
        }
    }
}
