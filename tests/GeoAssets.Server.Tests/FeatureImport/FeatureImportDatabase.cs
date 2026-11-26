using GeoAssets.Core.Models.Geometry;
using GeoAssets.Provider.PostgreSQL.Data;
using GeoAssets.Provider.PostgreSQL.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GeoAssets.Server.Tests.FeatureImport;

internal static class FeatureImportDatabase
{
    public static async Task<string> SeedAssetTypeAsync(string connectionString, Guid organizationId, GeometryType geometry)
    {
        var id = Guid.NewGuid();
        await using var db = new GeoAssetsDbContext(new DbContextOptionsBuilder<GeoAssetsDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.UseNetTopologySuite())
            .Options);
        db.Set<AssetTypeRow>().Add(new AssetTypeRow
        {
            Id = id,
            Name = "Import test type " + id.ToString("N"),
            AllowedGeometryType = geometry,
            OrganizationId = organizationId,
        });
        await db.SaveChangesAsync();
        return id.ToString();
    }

    public static async Task<FeatureImportEntity?> FindEntityAsync(string connectionString, string id)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT "Id", "Name", "AssetTypeId", "OrganizationId", custom_attributes::text, topology::text,
                   ST_SRID(geom), ST_X(geom), ST_Y(geom)
            FROM geo_entity WHERE "Id" = @id
            """, connection);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return new FeatureImportEntity(
            reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetGuid(3),
            reader.GetString(4), reader.GetString(5), reader.GetInt32(6), reader.GetDouble(7), reader.GetDouble(8));
    }

    public static async Task<IReadOnlyDictionary<string, string>> GetAttributeRowsAsync(string connectionString, string id)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""SELECT "Key", "Value" FROM geo_entity_attribute WHERE "EntityId" = @id""", connection);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new Dictionary<string, string>(StringComparer.Ordinal);
        while (await reader.ReadAsync())
        {
            rows[reader.GetString(0)] = reader.GetString(1);
        }

        return rows;
    }

    public static async Task<long> CountEntitiesAsync(string connectionString, Guid organizationId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""SELECT count(*) FROM geo_entity WHERE "OrganizationId" = @org""", connection);
        command.Parameters.AddWithValue("org", organizationId);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    public static async Task<IReadOnlyList<string>> GetRolesAsync(string connectionString, Guid assetTypeId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""SELECT "Roles" FROM asset_type WHERE "Id" = @id""", connection);
        command.Parameters.AddWithValue("id", assetTypeId);
        return (string[])(await command.ExecuteScalarAsync())!;
    }
}
