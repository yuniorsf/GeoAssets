using FluentAssertions;
using GeoAssets.Identity.Authorization.EFCore;
using GeoAssets.Workflow.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace GeoAssets.Security.Tests;

/// <summary>
/// Applies the <b>real</b> <see cref="ServiceOrderDbContext"/> migration chain carried by
/// <c>GeoAssets.Server</c> to an empty PostgreSQL database and proves the XD01-304 migration
/// (<c>AddOrderTypeTargetPolicyAndAttributesSchema</c>: nullable <c>text</c> columns
/// <c>"OrderTypes"."TargetPolicyJson"</c> and <c>"OrderTypes"."AttributesSchemaJson"</c>) applies
/// on top of its predecessor, keeps existing rows, and rolls back cleanly.
///
/// <c>GeoAssets.Server</c> is one of the two hosts that carry a migration history for this context
/// (the shared-database one; <c>GeoAssets.FieldService.Server</c> carries the isolated-database
/// one and has its own twin of this suite). Each fact migrates its own, freshly-named database
/// inside <see cref="SecurityTestContainerFixture"/>'s container, so rolling a migration back here
/// can never disturb the schema the row-level-security facts share.
/// </summary>
public class ServiceOrderMigrationChainTests
{
    private const string MigrationName = "AddOrderTypeTargetPolicyAndAttributesSchema";

    // SqlQueryRaw<string> maps a scalar result set through a column named "Value".
    private const string NewOrderTypeColumnsSql =
        """
        SELECT (column_name || ':' || data_type || ':' || is_nullable)::text AS "Value"
        FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name = 'OrderTypes'
          AND column_name IN ('TargetPolicyJson', 'AttributesSchemaJson')
        ORDER BY column_name
        """;

    private const string OrderTypeRowLevelSecuritySql =
        """
        SELECT (relrowsecurity::text || ':' || (SELECT count(*) FROM pg_policies WHERE tablename = 'OrderTypes')::text) AS "Value"
        FROM pg_class
        WHERE oid = '"OrderTypes"'::regclass
        """;

    [Fact]
    public async Task MigrateAsync_OnEmptyPostgres_AppliesEveryMigrationInTheChain()
    {
        await using var db = await NewContextOnEmptyDatabaseAsync();

        await db.Database.MigrateAsync();

        var chain = db.Database.GetMigrations().ToList();
        chain.Should().Contain(id => id.EndsWith("_" + MigrationName, StringComparison.Ordinal));
        (await db.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
        // In the shared database "__EFMigrationsHistory" also lists GeoIdentityDbContext's
        // migrations (applied by NewContextOnEmptyDatabaseAsync); only this context's own are compared.
        (await db.Database.GetAppliedMigrationsAsync()).Where(chain.Contains).Should().Equal(chain);
        (await QueryAsync(db, NewOrderTypeColumnsSql)).Should().Equal(
            "AttributesSchemaJson:text:YES", "TargetPolicyJson:text:YES");
    }

    [Fact]
    public async Task OrderTypeTargetPolicyMigration_AppliesOverExistingRows_ThenRollsBackAndReapplies()
    {
        await using var db = await NewContextOnEmptyDatabaseAsync();
        var migrator = db.GetService<IMigrator>();
        var chain = db.Database.GetMigrations().ToList();
        var target = chain.Single(id => id.EndsWith("_" + MigrationName, StringComparison.Ordinal));
        var previous = chain[chain.IndexOf(target) - 1];

        // The schema every deployed database has today, with one order type already stored.
        await migrator.MigrateAsync(previous);
        (await QueryAsync(db, NewOrderTypeColumnsSql)).Should().BeEmpty();
        await db.Database.ExecuteSqlRawAsync(
            """INSERT INTO "OrderTypes" ("Id", "DisplayName", "Description") VALUES ('inspection', 'Inspection', '')""");

        // Up.
        await migrator.MigrateAsync(target);
        (await QueryAsync(db, NewOrderTypeColumnsSql)).Should().Equal(
            "AttributesSchemaJson:text:YES", "TargetPolicyJson:text:YES");
        (await QueryAsync(db, """SELECT "Id"::text AS "Value" FROM "OrderTypes" WHERE "TargetPolicyJson" IS NULL AND "AttributesSchemaJson" IS NULL"""))
            .Should().Equal("inspection");
        await db.Database.ExecuteSqlRawAsync(
            """UPDATE "OrderTypes" SET "TargetPolicyJson" = '{{"targetAssetTypeIds":["pole"]}}', "AttributesSchemaJson" = '{{"type":"object"}}' WHERE "Id" = 'inspection'""");

        // Order types are not tenant-scoped: the migration must not have put RLS on the table.
        (await QueryAsync(db, OrderTypeRowLevelSecuritySql)).Should().Equal("false:0");

        // Down.
        await migrator.MigrateAsync(previous);
        (await QueryAsync(db, NewOrderTypeColumnsSql)).Should().BeEmpty();
        (await db.Database.GetAppliedMigrationsAsync()).Should().NotContain(target);
        (await QueryAsync(db, """SELECT "Id"::text AS "Value" FROM "OrderTypes" """)).Should().Equal("inspection");

        // Up again.
        await db.Database.MigrateAsync();
        (await db.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
        (await QueryAsync(db, NewOrderTypeColumnsSql)).Should().HaveCount(2);
    }

    private static Task<List<string>> QueryAsync(ServiceOrderDbContext db, string sql) =>
        db.Database.SqlQueryRaw<string>(sql).ToListAsync();

    /// <summary>
    /// A context on a database that holds only what this chain depends on from outside itself:
    /// the ServiceOrders RLS migrations reference "OrganizationGrants", which
    /// <see cref="GeoIdentityDbContext"/>'s own chain creates.
    /// </summary>
    private static async Task<ServiceOrderDbContext> NewContextOnEmptyDatabaseAsync()
    {
        var connectionString = new NpgsqlConnectionStringBuilder(await SecurityTestContainerFixture.EnsureStartedAsync())
        {
            Database = $"service_order_chain_{Guid.NewGuid():N}",
        }.ConnectionString;

        await using (var identity = new GeoIdentityDbContext(SecurityTestContainerFixture.BuildGeoIdentityOptions(connectionString)))
        {
            await identity.Database.MigrateAsync();
        }

        return new ServiceOrderDbContext(SecurityTestContainerFixture.BuildServiceOrderOptions(connectionString));
    }
}
