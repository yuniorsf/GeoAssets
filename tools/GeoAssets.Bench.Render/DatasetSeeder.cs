using Npgsql;
using NpgsqlTypes;

namespace GeoAssets.Bench.Render;

/// <summary>What <see cref="DatasetSeeder.SeedAsync"/> wrote.</summary>
internal sealed record SeedSummary(IReadOnlyList<(BenchAssetType Type, int Count)> Counts, long TotalVertices, TimeSpan Elapsed);

/// <summary>
/// Writes the benchmark dataset (XD01-420) into a database whose schema <c>dotnet ef database update</c> applied
/// (<c>GeoAssetsDbContext</c>'s chain): replaces every <c>geo_entity</c> row and the benchmark's asset types, then
/// <c>ANALYZE</c>s, so each run starts from the same table. Refuses a database whose name does not contain
/// <c>bench</c> — it empties <c>geo_entity</c>.
/// </summary>
internal sealed class DatasetSeeder(NpgsqlDataSource dataSource, TimeProvider timeProvider, TextWriter log)
{
    public async Task<SeedSummary> SeedAsync(DatasetGenerator generator, int totalFeatures, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(generator);

        var started = timeProvider.GetTimestamp();
        var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            EnsureBenchDatabase(connection.Database);
            await EnsureSchemaAsync(connection, cancellationToken).ConfigureAwait(false);

            await log.WriteLineAsync($"Replacing geo_entity with {totalFeatures:N0} generated features...").ConfigureAwait(false);
            await ExecuteAsync(connection, "TRUNCATE TABLE geo_entity CASCADE", cancellationToken).ConfigureAwait(false);
            await WriteAssetTypesAsync(connection, cancellationToken).ConfigureAwait(false);
            var totalVertices = await CopyFeaturesAsync(connection, generator.Generate(totalFeatures), cancellationToken).ConfigureAwait(false);

            await log.WriteLineAsync("ANALYZE...").ConfigureAwait(false);
            await ExecuteAsync(connection, "ANALYZE geo_entity; ANALYZE asset_type", cancellationToken).ConfigureAwait(false);

            return new SeedSummary(DatasetComposition.Counts(totalFeatures), totalVertices, timeProvider.GetElapsedTime(started));
        }
    }

    internal static void EnsureBenchDatabase(string database)
    {
        if (!database.Contains("bench", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Refusing to seed database '{database}': seeding empties geo_entity, so the database name must contain 'bench'.");
        }
    }

    private static async Task EnsureSchemaAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT to_regclass('public.geo_entity') IS NOT NULL AND to_regclass('public.asset_type') IS NOT NULL",
            connection);
        if (await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not true)
        {
            throw new InvalidOperationException(
                "geo_entity/asset_type are missing — apply GeoAssetsDbContext's migrations first (see docs/perf/render-baseline.md, 'Re-running').");
        }
    }

    private static async Task WriteAssetTypesAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        var ids = DatasetComposition.AssetTypes.Select(t => t.Id).ToArray();
        await using (var delete = new NpgsqlCommand("""DELETE FROM asset_type WHERE "Id" = ANY(@ids)""", connection))
        {
            delete.Parameters.AddWithValue("ids", ids);
            await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (var type in DatasetComposition.AssetTypes)
        {
            await using var insert = new NpgsqlCommand(
                """
                INSERT INTO asset_type ("Id", "Name", "Color", "IsBuiltIn", "MinZoom", "MaxZoom")
                VALUES (@id, @name, @color, false, @minZoom, @maxZoom)
                """,
                connection);
            insert.Parameters.AddWithValue("id", type.Id);
            insert.Parameters.AddWithValue("name", type.Name);
            insert.Parameters.AddWithValue("color", type.Color);
            insert.Parameters.Add(new NpgsqlParameter<int?>("minZoom", NpgsqlDbType.Integer) { TypedValue = type.MinZoom });
            insert.Parameters.Add(new NpgsqlParameter<int?>("maxZoom", NpgsqlDbType.Integer) { TypedValue = type.MaxZoom });
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<long> CopyFeaturesAsync(
        NpgsqlConnection connection, IEnumerable<BenchFeatureRow> rows, CancellationToken cancellationToken)
    {
        long vertices = 0;
        var importer = await connection.BeginBinaryImportAsync(
            """
            COPY geo_entity ("Id", "Name", "AssetTypeId", "Description", "OrganizationId", "CreatedAt", "UpdatedAt", geom, custom_attributes, topology)
            FROM STDIN (FORMAT BINARY)
            """,
            cancellationToken).ConfigureAwait(false);
        await using (importer.ConfigureAwait(false))
        {
            foreach (var row in rows)
            {
                await importer.StartRowAsync(cancellationToken).ConfigureAwait(false);
                await importer.WriteAsync(row.Id, NpgsqlDbType.Varchar, cancellationToken).ConfigureAwait(false);
                await importer.WriteAsync(row.Name, NpgsqlDbType.Varchar, cancellationToken).ConfigureAwait(false);
                await importer.WriteAsync(row.AssetType.Id.ToString(), NpgsqlDbType.Varchar, cancellationToken).ConfigureAwait(false);
                await importer.WriteAsync(row.Description, NpgsqlDbType.Varchar, cancellationToken).ConfigureAwait(false);
                await importer.WriteAsync(Guid.Empty, NpgsqlDbType.Uuid, cancellationToken).ConfigureAwait(false);
                await importer.WriteAsync(row.CreatedAt, NpgsqlDbType.TimestampTz, cancellationToken).ConfigureAwait(false);
                await importer.WriteAsync(row.UpdatedAt, NpgsqlDbType.TimestampTz, cancellationToken).ConfigureAwait(false);
                await importer.WriteAsync(row.Geometry, NpgsqlDbType.Geometry, cancellationToken).ConfigureAwait(false);
                await importer.WriteAsync(row.CustomAttributesJson, NpgsqlDbType.Jsonb, cancellationToken).ConfigureAwait(false);
                await importer.WriteAsync("[]", NpgsqlDbType.Jsonb, cancellationToken).ConfigureAwait(false);
                vertices += row.Geometry.NumPoints;
            }

            await importer.CompleteAsync(cancellationToken).ConfigureAwait(false);
        }

        return vertices;
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 600 };
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
