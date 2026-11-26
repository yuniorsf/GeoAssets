using GeoAssets.Core.FeatureImport;
using GeoAssets.Provider.PostgreSQL.Entities;
using Npgsql;
using NpgsqlTypes;

namespace GeoAssets.Provider.PostgreSQL.Repositories;

/// <summary>
/// The SQL of one <see cref="PostgresFeatureBulkImporter"/> chunk, run on the connection of the chunk's open transaction:
/// the transaction-local tenant setting the <c>geo_entity</c> Row-Level Security policy reads, a binary <c>COPY</c> into a
/// transaction-scoped staging table, the upsert into <c>geo_entity</c> (refusing ids another organization owns) and the
/// rewrite of the written ids' <c>geo_entity_attribute</c> rows from the staged <c>custom_attributes</c>.
/// </summary>
internal static class FeatureImportStage
{
    private const string SetOrganizationSql =
        "SELECT set_config('app.current_organization_id', @organizationId, true)";

    private const string CreateStageSql = """
        CREATE TEMP TABLE feature_import_stage (
            "Id" varchar(36) NOT NULL,
            "Name" varchar(256) NOT NULL,
            "AssetTypeId" varchar(36) NOT NULL,
            "Description" text NOT NULL,
            "LayerId" uuid NULL,
            "OrganizationId" uuid NOT NULL,
            "CreatedAt" timestamp with time zone NOT NULL,
            "UpdatedAt" timestamp with time zone NOT NULL,
            geom geometry NULL,
            custom_attributes jsonb NOT NULL,
            topology jsonb NOT NULL
        ) ON COMMIT DROP
        """;

    private const string CopySql = """
        COPY feature_import_stage ("Id", "Name", "AssetTypeId", "Description", "LayerId", "OrganizationId",
                                   "CreatedAt", "UpdatedAt", geom, custom_attributes, topology)
        FROM STDIN (FORMAT BINARY)
        """;

    private const string UpsertSql = """
        INSERT INTO geo_entity ("Id", "Name", "AssetTypeId", "Description", "LayerId", "OrganizationId",
                                "CreatedAt", "UpdatedAt", geom, custom_attributes, topology)
        SELECT "Id", "Name", "AssetTypeId", "Description", "LayerId", "OrganizationId",
               "CreatedAt", "UpdatedAt", geom, custom_attributes, topology
        FROM feature_import_stage
        ON CONFLICT ("Id") DO UPDATE SET
            "Name" = EXCLUDED."Name",
            "AssetTypeId" = EXCLUDED."AssetTypeId",
            "Description" = EXCLUDED."Description",
            "LayerId" = EXCLUDED."LayerId",
            "UpdatedAt" = EXCLUDED."UpdatedAt",
            geom = EXCLUDED.geom,
            custom_attributes = EXCLUDED.custom_attributes,
            topology = EXCLUDED.topology
        WHERE geo_entity."OrganizationId" = EXCLUDED."OrganizationId"
        RETURNING "Id", (xmax = 0) AS inserted
        """;

    private const string DeleteAttributesSql =
        """DELETE FROM geo_entity_attribute WHERE "EntityId" = ANY(@ids)""";

    private const string InsertAttributesSql = """
        INSERT INTO geo_entity_attribute ("EntityId", "Key", "Value")
        SELECT s."Id", kv.key, kv.value
        FROM feature_import_stage s
        CROSS JOIN LATERAL jsonb_each_text(s.custom_attributes) AS kv
        WHERE s."Id" = ANY(@ids) AND kv.value IS NOT NULL
        """;

    private const string DeleteScopeSql = """
        DELETE FROM geo_entity
        WHERE "OrganizationId" = @organizationId
          AND custom_attributes @> jsonb_build_object(@scopeKey, @scope)
          AND custom_attributes ->> @importIdKey <> @importId
        """;

    /// <summary>Stamps <paramref name="organizationId"/> on the open transaction (<c>set_config(…, is_local: true)</c>).</summary>
    internal static async Task SetOrganizationAsync(NpgsqlConnection connection, Guid organizationId, CancellationToken ct)
    {
        var command = new NpgsqlCommand(SetOrganizationSql, connection);
        await using (command.ConfigureAwait(false))
        {
            command.Parameters.Add(new NpgsqlParameter("organizationId", NpgsqlDbType.Text) { Value = organizationId.ToString() });
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    /// <summary>Creates the staging table (dropped when the transaction commits) and binary-copies <paramref name="rows"/> into it.</summary>
    internal static async Task StageAsync(NpgsqlConnection connection, IReadOnlyList<GeoEntityRow> rows, CancellationToken ct)
    {
        var create = new NpgsqlCommand(CreateStageSql, connection);
        await using (create.ConfigureAwait(false))
            await create.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

        var writer = await connection.BeginBinaryImportAsync(CopySql, ct).ConfigureAwait(false);
        await using (writer.ConfigureAwait(false))
        {
            foreach (var row in rows)
                await WriteRowAsync(writer, row, ct).ConfigureAwait(false);

            await writer.CompleteAsync(ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Upserts the staged rows into <c>geo_entity</c>; answers each written id with whether it was inserted (<c>true</c>)
    /// or updated. A staged id missing from the answer belongs to another organization and was left as it was.
    /// </summary>
    internal static async Task<IReadOnlyDictionary<string, bool>> UpsertAsync(NpgsqlConnection connection, CancellationToken ct)
    {
        var written = new Dictionary<string, bool>(StringComparer.Ordinal);
        var command = new NpgsqlCommand(UpsertSql, connection);
        await using (command.ConfigureAwait(false))
        {
            var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            await using (reader.ConfigureAwait(false))
            {
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                    written[reader.GetString(0)] = reader.GetBoolean(1);
            }
        }

        return written;
    }

    /// <summary>Replaces <paramref name="ids"/>' <c>geo_entity_attribute</c> rows with their staged <c>custom_attributes</c>.</summary>
    internal static async Task ReplaceAttributesAsync(NpgsqlConnection connection, string[] ids, CancellationToken ct)
    {
        var delete = new NpgsqlCommand(DeleteAttributesSql, connection);
        await using (delete.ConfigureAwait(false))
        {
            delete.Parameters.Add(IdsParameter(ids));
            await delete.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        var insert = new NpgsqlCommand(InsertAttributesSql, connection);
        await using (insert.ConfigureAwait(false))
        {
            insert.Parameters.Add(IdsParameter(ids));
            await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Deletes the organization's features of <paramref name="scope"/> that another import than <paramref name="importId"/>
    /// wrote (their <c>geo_entity_attribute</c> rows cascade); answers how many.
    /// </summary>
    internal static async Task<int> DeleteScopeAsync(
        NpgsqlConnection connection, Guid organizationId, string scope, string importId, CancellationToken ct)
    {
        var command = new NpgsqlCommand(DeleteScopeSql, connection);
        await using (command.ConfigureAwait(false))
        {
            command.Parameters.Add(new NpgsqlParameter("organizationId", NpgsqlDbType.Uuid) { Value = organizationId });
            command.Parameters.Add(new NpgsqlParameter("scopeKey", NpgsqlDbType.Text) { Value = FeatureImportAttributes.Scope });
            command.Parameters.Add(new NpgsqlParameter("scope", NpgsqlDbType.Text) { Value = scope });
            command.Parameters.Add(new NpgsqlParameter("importIdKey", NpgsqlDbType.Text) { Value = FeatureImportAttributes.ImportId });
            command.Parameters.Add(new NpgsqlParameter("importId", NpgsqlDbType.Text) { Value = importId });
            return await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    private static NpgsqlParameter IdsParameter(string[] ids) =>
        new("ids", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = ids };

    private static async Task WriteRowAsync(NpgsqlBinaryImporter writer, GeoEntityRow row, CancellationToken ct)
    {
        await writer.StartRowAsync(ct).ConfigureAwait(false);
        await writer.WriteAsync(row.Id, NpgsqlDbType.Varchar, ct).ConfigureAwait(false);
        await writer.WriteAsync(row.Name, NpgsqlDbType.Varchar, ct).ConfigureAwait(false);
        await writer.WriteAsync(row.AssetTypeId, NpgsqlDbType.Varchar, ct).ConfigureAwait(false);
        await writer.WriteAsync(row.Description, NpgsqlDbType.Text, ct).ConfigureAwait(false);

        if (row.LayerId is { } layerId)
            await writer.WriteAsync(layerId, NpgsqlDbType.Uuid, ct).ConfigureAwait(false);
        else
            await writer.WriteNullAsync(ct).ConfigureAwait(false);

        await writer.WriteAsync(row.OrganizationId, NpgsqlDbType.Uuid, ct).ConfigureAwait(false);
        await writer.WriteAsync(row.CreatedAt, NpgsqlDbType.TimestampTz, ct).ConfigureAwait(false);
        await writer.WriteAsync(row.UpdatedAt, NpgsqlDbType.TimestampTz, ct).ConfigureAwait(false);

        if (row.Geom is { } geometry)
            await writer.WriteAsync(geometry, NpgsqlDbType.Geometry, ct).ConfigureAwait(false);
        else
            await writer.WriteNullAsync(ct).ConfigureAwait(false);

        await writer.WriteAsync(row.CustomAttributesJson, NpgsqlDbType.Jsonb, ct).ConfigureAwait(false);
        await writer.WriteAsync(row.TopologyJson, NpgsqlDbType.Jsonb, ct).ConfigureAwait(false);
    }
}
