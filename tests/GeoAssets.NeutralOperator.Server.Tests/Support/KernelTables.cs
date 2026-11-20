using Npgsql;

namespace GeoAssets.NeutralOperator.Server.Tests.Support;

/// <summary>Writes the host's order tables directly, as their owner, the way the chassis' stores would — and reads them back as the non-owner application role.</summary>
internal static class KernelTables
{
    public const string ScopedRole = "neutral_operator_host_scoped_role";

    private const string InsertOrderSql =
        """
        INSERT INTO "ServiceOrders" ("Id", "Title", "Description", "OrderTypeId", "Status", "Priority", "CreatedBy", "OrganizationId", "CreatedAt", "AttributesJson", "FeatureIdsJson", "RowVersion")
        VALUES (@id, @id, '', 'shared-use-pole-work', 'Draft', 0, 'tester', @orgId, now(), '{}', '[]', E'\\x01')
        """;

    private const string InsertTargetSql =
        """
        INSERT INTO "ServiceOrderTargets" ("Id", "ServiceOrderId", "FeatureId", "AssetTypeId", "ActivityIdsJson", "Status", "ConflictLevel", "AddedAt", "AddedBy", "OrganizationId", "Version")
        VALUES (@id, @orderId, @featureId, 'pole', '[]', 'Pending', 0, now(), 'tester', @orgId, 0)
        """;

    private const string InsertActionLogSql =
        """
        INSERT INTO "OrderActionLogs" ("ServiceOrderId", "PerformedBy", "PerformedAt", "ActorKind") VALUES (@orderId, 'tester', now(), 0)
        """;

    private const string InsertDispatchSql =
        """
        INSERT INTO "OrderDispatches" ("ServiceOrderId", "TargetId", "TargetType", "DispatchedBy", "DispatchedAt", "ActorKind") VALUES (@orderId, @target, 2, 'tester', now(), 0)
        """;

    private const string InsertChangeSetSql =
        """
        INSERT INTO neutral_operator."ServiceOrderOccupancyChangeSets"
            ("Id", "ServiceOrderId", "TargetId", "PoleFeatureId", "ChangesJson", "BaselineVersion", "Status", "StagedBy", "StagedAt", "ClosedAt", "OrganizationId")
        VALUES (@id, @orderId, @targetId, 'pole-a', '[]', 0, 'Staged', 'tester', now(), NULL, @orgId)
        """;

    private const string InsertGrantSql =
        """
        INSERT INTO "OrganizationGrantMirror" ("GrantId", "GranteeOrganizationId", "ResourceOrganizationId", "ResourceType", "ScopeId", "IsActive", "ExpiresAt", "SyncedAtUtc")
        VALUES (@id, @grantee, @resource, NULL, NULL, @isActive, NULL, now())
        """;

    private const string UpdateTitleSql = """UPDATE "ServiceOrders" SET "Title" = 'hijacked' WHERE "Id" = @id""";

    private const string MoveOrderSql = """UPDATE "ServiceOrders" SET "OrganizationId" = @orgId WHERE "Id" = @id""";

    private const string DeleteOrderSql = """DELETE FROM "ServiceOrders" WHERE "Id" = @id""";

    private const string GrantSql =
        $"""
        DO $$
        BEGIN
           IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{ScopedRole}') THEN
              CREATE ROLE {ScopedRole} LOGIN PASSWORD '{ScopedRole}';
           END IF;
        END $$;

        GRANT USAGE ON SCHEMA public TO {ScopedRole};
        GRANT SELECT, INSERT, UPDATE, DELETE ON "ServiceOrders", "ServiceOrderTargets", "OrderActionLogs", "OrderDispatches" TO {ScopedRole};
        GRANT SELECT ON "OrganizationGrantMirror", "OrderTypes", "OrderTypeRecipientActions" TO {ScopedRole};
        GRANT USAGE ON ALL SEQUENCES IN SCHEMA public TO {ScopedRole};
        GRANT USAGE ON SCHEMA neutral_operator TO {ScopedRole};
        GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA neutral_operator TO {ScopedRole};
        """;

    private static readonly SemaphoreSlim _roleLock = new(1, 1);

    public static async Task<NpgsqlConnection> OpenAsOwnerAsync(string connectionString)
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        return connection;
    }

    /// <summary>
    /// Row-level security does not restrict a table's owner (no FORCE, as everywhere in this repository), so it is only observable as
    /// a second, non-owner role with the privileges an application role would need.
    /// </summary>
    public static async Task<NpgsqlConnection> OpenAsScopedRoleAsync(string connectionString)
    {
        await _roleLock.WaitAsync();
        try
        {
            await using var owner = await OpenAsOwnerAsync(connectionString);
            await using var grant = owner.CreateCommand();
            grant.CommandText = GrantSql;
            await grant.ExecuteNonQueryAsync();
        }
        finally
        {
            _roleLock.Release();
        }

        var builder = new NpgsqlConnectionStringBuilder(connectionString) { Username = ScopedRole, Password = ScopedRole, Pooling = false };
        var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    public static async Task SetOrganizationAsync(NpgsqlConnection connection, Guid organizationId)
    {
        await using var setConfig = connection.CreateCommand();
        setConfig.CommandText = "SELECT set_config('app.current_organization_id', @orgId, false)";
        setConfig.Parameters.Add(new NpgsqlParameter("orgId", organizationId.ToString()));
        await setConfig.ExecuteNonQueryAsync();
    }

    public static async Task InsertOrderAsync(NpgsqlConnection connection, string id, Guid organizationId)
    {
        await using var insert = connection.CreateCommand();
        insert.CommandText = InsertOrderSql;
        insert.Parameters.Add(new NpgsqlParameter("id", id));
        insert.Parameters.Add(new NpgsqlParameter("orgId", organizationId));
        await insert.ExecuteNonQueryAsync();
    }

    public static async Task InsertTargetAsync(NpgsqlConnection connection, string id, string orderId, Guid organizationId, string featureId = "pole-1")
    {
        await using var insert = connection.CreateCommand();
        insert.CommandText = InsertTargetSql;
        insert.Parameters.Add(new NpgsqlParameter("id", id));
        insert.Parameters.Add(new NpgsqlParameter("orderId", orderId));
        insert.Parameters.Add(new NpgsqlParameter("featureId", featureId));
        insert.Parameters.Add(new NpgsqlParameter("orgId", organizationId));
        await insert.ExecuteNonQueryAsync();
    }

    public static async Task InsertActionLogAsync(NpgsqlConnection connection, string orderId)
    {
        await using var insert = connection.CreateCommand();
        insert.CommandText = InsertActionLogSql;
        insert.Parameters.Add(new NpgsqlParameter("orderId", orderId));
        await insert.ExecuteNonQueryAsync();
    }

    public static async Task InsertDispatchAsync(NpgsqlConnection connection, string orderId, Guid recipientOrganization)
    {
        await using var insert = connection.CreateCommand();
        insert.CommandText = InsertDispatchSql;
        insert.Parameters.Add(new NpgsqlParameter("orderId", orderId));
        insert.Parameters.Add(new NpgsqlParameter("target", recipientOrganization.ToString()));
        await insert.ExecuteNonQueryAsync();
    }

    public static async Task InsertChangeSetAsync(NpgsqlConnection connection, string orderId, string targetId, Guid organizationId)
    {
        await using var insert = connection.CreateCommand();
        insert.CommandText = InsertChangeSetSql;
        insert.Parameters.Add(new NpgsqlParameter("id", Guid.NewGuid()));
        insert.Parameters.Add(new NpgsqlParameter("orderId", orderId));
        insert.Parameters.Add(new NpgsqlParameter("targetId", targetId));
        insert.Parameters.Add(new NpgsqlParameter("orgId", organizationId));
        await insert.ExecuteNonQueryAsync();
    }

    /// <summary>A grant from <paramref name="resourceOrganization"/> to <paramref name="grantee"/>, as the grant sync would mirror it.</summary>
    public static async Task InsertGrantAsync(NpgsqlConnection connection, Guid grantee, Guid resourceOrganization, bool isActive)
    {
        await using var insert = connection.CreateCommand();
        insert.CommandText = InsertGrantSql;
        insert.Parameters.Add(new NpgsqlParameter("id", Guid.NewGuid()));
        insert.Parameters.Add(new NpgsqlParameter("grantee", grantee));
        insert.Parameters.Add(new NpgsqlParameter("resource", resourceOrganization));
        insert.Parameters.Add(new NpgsqlParameter("isActive", isActive));
        await insert.ExecuteNonQueryAsync();
    }

    public static async Task<int> UpdateTitleAsync(NpgsqlConnection connection, string orderId)
    {
        await using var update = connection.CreateCommand();
        update.CommandText = UpdateTitleSql;
        update.Parameters.Add(new NpgsqlParameter("id", orderId));
        return await update.ExecuteNonQueryAsync();
    }

    public static async Task<int> MoveOrderAsync(NpgsqlConnection connection, string orderId, Guid organizationId)
    {
        await using var update = connection.CreateCommand();
        update.CommandText = MoveOrderSql;
        update.Parameters.Add(new NpgsqlParameter("id", orderId));
        update.Parameters.Add(new NpgsqlParameter("orgId", organizationId));
        return await update.ExecuteNonQueryAsync();
    }

    public static async Task<int> DeleteOrderAsync(NpgsqlConnection connection, string orderId)
    {
        await using var delete = connection.CreateCommand();
        delete.CommandText = DeleteOrderSql;
        delete.Parameters.Add(new NpgsqlParameter("id", orderId));
        return await delete.ExecuteNonQueryAsync();
    }

    public static async Task<List<string>> VisibleOrdersAsync(NpgsqlConnection connection)
    {
        await using var select = connection.CreateCommand();
        select.CommandText = """SELECT "Id" FROM "ServiceOrders" ORDER BY "Id" COLLATE "C" """;
        return await ReadAsync(select);
    }

    public static async Task<List<string>> VisibleTargetsAsync(NpgsqlConnection connection)
    {
        await using var select = connection.CreateCommand();
        select.CommandText = """SELECT "Id" FROM "ServiceOrderTargets" ORDER BY "Id" COLLATE "C" """;
        return await ReadAsync(select);
    }

    public static async Task<List<string>> VisibleActionLogOrdersAsync(NpgsqlConnection connection)
    {
        await using var select = connection.CreateCommand();
        select.CommandText = """SELECT "ServiceOrderId" FROM "OrderActionLogs" ORDER BY "ServiceOrderId" COLLATE "C" """;
        return await ReadAsync(select);
    }

    public static async Task<List<string>> VisibleDispatchOrdersAsync(NpgsqlConnection connection)
    {
        await using var select = connection.CreateCommand();
        select.CommandText = """SELECT "ServiceOrderId" FROM "OrderDispatches" ORDER BY "ServiceOrderId" COLLATE "C" """;
        return await ReadAsync(select);
    }

    public static async Task<List<string>> VisiblePoleOccupanciesAsync(NpgsqlConnection connection)
    {
        await using var select = connection.CreateCommand();
        select.CommandText = """SELECT "PoleFeatureId" FROM neutral_operator.field_pole_occupancies ORDER BY "PoleFeatureId" COLLATE "C" """;
        return await ReadAsync(select);
    }

    public static async Task<List<string>> VisibleChangeSetOrdersAsync(NpgsqlConnection connection)
    {
        await using var select = connection.CreateCommand();
        select.CommandText = """SELECT "ServiceOrderId" FROM neutral_operator."ServiceOrderOccupancyChangeSets" ORDER BY "ServiceOrderId" COLLATE "C" """;
        return await ReadAsync(select);
    }

    private static async Task<List<string>> ReadAsync(NpgsqlCommand select)
    {
        var values = new List<string>();
        await using var reader = await select.ExecuteReaderAsync();
        while (await reader.ReadAsync()) values.Add(reader.GetString(0));
        return values;
    }
}
