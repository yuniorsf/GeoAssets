using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace GeoAssets.FieldService.Server.Tests;

/// <summary>
/// The XD01-333 migration <c>AddOrderTypeRecipientAccess</c> in the
/// <c>GeoAssets.FieldService.Server</c> history: its column, functions and policies, up, down and
/// up again; and — as a non-owner role — that the organization an order was dispatched to writes
/// the order only when its type is Edit (2), and the order's targets only when it is Execute (1)
/// or Edit, while the owner and a mirrored grantee write both whatever the type says.
/// </summary>
public partial class FieldServiceMigrationChainTests
{
    private const string RecipientAccessMigration = "AddOrderTypeRecipientAccess";

    private const string RecipientUpdatePoliciesSql =
        """
        SELECT (tablename || '|' || policyname || '|' || cmd || '|' || coalesce(qual, '') || '|' || coalesce(with_check, ''))::text AS "Value"
        FROM pg_policies
        WHERE schemaname = 'public' AND policyname = 'dispatch_recipient_update_policy'
        ORDER BY tablename COLLATE "C"
        """;

    private static readonly string[] LevelOrders = ["ra-inform", "ra-execute", "ra-edit", "ra-no-type-row"];

    [Fact]
    public async Task RecipientAccessMigration_AddsItsColumnFunctionsAndPolicies_ThenRollsBackAndReapplies()
    {
        await using var db = await NewContextOnEmptyDatabaseAsync();
        var migrator = db.GetService<IMigrator>();
        var chain = db.Database.GetMigrations().ToList();
        var migration = MigrationId(db, RecipientAccessMigration);
        var previous = chain[chain.IndexOf(migration) - 1];
        previous.Should().Be(MigrationId(db, DispatchRecipientMigration));

        await migrator.MigrateAsync(previous);
        await using (var owner = await OpenAsync(db))
            await ExecuteAsync(owner, """INSERT INTO "OrderTypes" ("Id", "DisplayName", "Description") VALUES ('inspection', 'Inspection', '')""");
        var updatePolicyBefore = await QueryAsync(db, RecipientUpdatePoliciesSql);
        updatePolicyBefore.Should().ContainSingle().Which.Should().StartWith("ServiceOrders|");

        // Up.
        await migrator.MigrateAsync(migration);
        (await QueryAsync(db, """SELECT ("Id" || ':' || "RecipientAccess"::text)::text AS "Value" FROM "OrderTypes" """)).Should().Equal("inspection:0");
        (await QueryAsync(db, DispatchRecipientFunctionsSql)).Should().Equal(
            "service_order_dispatched_to_current_organization|true|s|search_path=public, pg_temp",
            "service_order_dispatched_to_current_organization_with_access|true|s|search_path=public, pg_temp",
            "service_order_keeps_organization|true|s|search_path=public, pg_temp",
            "service_order_owned_by_or_granted_to_current_organization|true|s|search_path=public, pg_temp",
            "service_order_target_stays_on_order|true|s|search_path=public, pg_temp");
        var updatePolicies = await QueryAsync(db, RecipientUpdatePoliciesSql);
        updatePolicies.Should().HaveCount(2);
        // PostgreSQL prints the policies back with its own casts, so only the calls and levels are matched.
        updatePolicies[0].Should().StartWith("ServiceOrderTargets|")
            .And.Contain("service_order_dispatched_to_current_organization_with_access(").And.Contain(", 1)")
            .And.Contain("service_order_target_stays_on_order(").And.Contain("service_order_keeps_organization(");
        updatePolicies[1].Should().StartWith("ServiceOrders|")
            .And.Contain("service_order_dispatched_to_current_organization_with_access(").And.EndWith(", 2)");
        (await QueryAsync(db, DispatchRecipientPoliciesSql)).Should().HaveCount(9);

        // Down.
        await migrator.MigrateAsync(previous);
        (await QueryAsync(db, RecipientUpdatePoliciesSql)).Should().Equal(updatePolicyBefore);
        (await QueryAsync(db, DispatchRecipientFunctionsSql)).Should().HaveCount(3);
        (await QueryAsync(db, """SELECT column_name::text AS "Value" FROM information_schema.columns WHERE table_name = 'OrderTypes' AND column_name = 'RecipientAccess'"""))
            .Should().BeEmpty();
        (await QueryAsync(db, """SELECT "Id"::text AS "Value" FROM "OrderTypes" """)).Should().Equal("inspection");

        // Up again.
        await db.Database.MigrateAsync();
        (await db.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
        (await QueryAsync(db, RecipientUpdatePoliciesSql)).Should().HaveCount(2);
    }

    [Fact]
    public async Task RecipientAccessPolicies_AsANonOwnerRole_LetTheRecipientWriteWhatItsOrdersTypeAllows_AndTheOwnerAndAGranteeEverything()
    {
        await using var db = await NewContextOnEmptyDatabaseAsync();
        await db.Database.MigrateAsync();
        await SeedLevelsAsync(db);
        await using var scoped = await OpenAsTenantScopedRoleAsync(db);

        await SetOrganizationAsync(scoped, FirstRecipientOrganization);
        (await TouchOrdersAsync(scoped)).Should().Equal(0, 0, 1, 0);
        (await CompleteTargetsAsync(scoped)).Should().Equal(0, 1, 1, 0);
        var add = async () => await InsertTargetAsync(scoped, "tgt-forged", "ra-edit", OwnerOrganization, "feat-9");
        (await add.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(RowSecurityViolation, "no level adds a target");
        (await DeleteTargetAsync(scoped, "tgt-ra-edit")).Should().Be(0, "nor removes one");
        var move = async () => await ExecuteAsync(scoped, """UPDATE "ServiceOrderTargets" SET "ServiceOrderId" = 'ra-edit' WHERE "Id" = 'tgt-ra-execute'""");
        (await move.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(RowSecurityViolation, "a target stays on its order");
        var relabel = async () => await ExecuteAsync(scoped, """UPDATE "ServiceOrders" SET "OrganizationId" = @org WHERE "Id" = 'ra-edit'""", ("org", FirstRecipientOrganization));
        (await relabel.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(RowSecurityViolation, "even an Edit recipient keeps the order's organization");

        await SetOrganizationAsync(scoped, SecondRecipientOrganization);
        (await TouchOrdersAsync(scoped)).Should().Equal([0, 0, 0, 0], "nothing was dispatched to it");
        (await CompleteTargetsAsync(scoped)).Should().Equal(0, 0, 0, 0);

        await SetOrganizationAsync(scoped, OwnerOrganization);
        (await TouchOrdersAsync(scoped)).Should().Equal(1, 1, 1, 1);
        (await CompleteTargetsAsync(scoped)).Should().Equal(1, 1, 1, 1);

        await using (var owner = await OpenAsync(db))
            await InsertMirroredGrantAsync(owner, isActive: true, isExpired: false, resourceType: "ServiceOrder", scopeId: null);
        await SetOrganizationAsync(scoped, GranteeOrganization);
        (await TouchOrdersAsync(scoped)).Should().Equal(1, 1, 1, 1);
        (await CompleteTargetsAsync(scoped)).Should().Equal(1, 1, 1, 1);
    }

    /// <summary>
    /// As the table owner: order types at levels 0, 1 and 2, and one order of each plus one of a
    /// type with no row — each with a target and dispatched to the first recipient organization.
    /// </summary>
    private static async Task SeedLevelsAsync(Workflow.Persistence.ServiceOrderDbContext db)
    {
        await using var owner = await OpenAsync(db);
        await ExecuteAsync(owner,
            """
            INSERT INTO "OrderTypes" ("Id", "DisplayName", "Description", "RecipientAccess") VALUES
                ('rls-inform', 'Inform', '', 0), ('rls-execute', 'Execute', '', 1), ('rls-edit', 'Edit', '', 2)
            """);
        string[] types = ["rls-inform", "rls-execute", "rls-edit", "rls-no-such-type"];
        for (var i = 0; i < LevelOrders.Length; i++)
        {
            await InsertOrderAsync(owner, LevelOrders[i], OwnerOrganization);
            await ExecuteAsync(owner, """UPDATE "ServiceOrders" SET "OrderTypeId" = @type WHERE "Id" = @id""", ("type", types[i]), ("id", LevelOrders[i]));
            await InsertTargetAsync(owner, $"tgt-{LevelOrders[i]}", LevelOrders[i], OwnerOrganization, $"feat-{i}");
            await ExecuteAsync(owner, DispatchToOrganizationSql, ("orderId", LevelOrders[i]), ("targetId", FirstRecipientOrganization.ToString()), ("targetType", 2));
        }
    }

    private static async Task<List<int>> TouchOrdersAsync(NpgsqlConnection connection)
    {
        var updated = new List<int>();
        foreach (var orderId in LevelOrders)
            updated.Add(await ExecuteAsync(connection, """UPDATE "ServiceOrders" SET "Title" = 'Edited' WHERE "Id" = @id""", ("id", orderId)));
        return updated;
    }

    private static async Task<List<int>> CompleteTargetsAsync(NpgsqlConnection connection)
    {
        var updated = new List<int>();
        foreach (var orderId in LevelOrders)
            updated.Add(await ExecuteAsync(connection, """UPDATE "ServiceOrderTargets" SET "Status" = 'Done', "Note" = 'worked' WHERE "ServiceOrderId" = @id""", ("id", orderId)));
        return updated;
    }
}
