using FluentAssertions;
using GeoAssets.Identity.Authorization.EFCore;
using GeoAssets.Identity.Authorization.EFCore.Repositories;
using GeoAssets.Identity.Authorization.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace GeoAssets.Security.Tests;

/// <summary>
/// Applies the <b>real</b> <see cref="GeoIdentityDbContext"/> migration chain carried by <c>GeoAssets.Server</c> to
/// PostgreSQL and proves the XD01-326 migration (<c>AddOrganizationGrantScopedRoleAndOrderTypes</c>) leaves every grant
/// that already exists exactly as it was — organization-wide (<c>"RequiresScopedRole"</c> false) and for every order type
/// (<c>"OrderTypeIds"</c> <c>'[]'</c>) — read back through the repository the kernel uses, that the new fields then round-trip,
/// and that it rolls back.
///
/// Each fact migrates its own, freshly-named database inside <see cref="SecurityTestContainerFixture"/>'s container.
/// </summary>
public class OrganizationGrantScopedRoleMigrationTests
{
    private const string MigrationName = "AddOrganizationGrantScopedRoleAndOrderTypes";

    private static readonly Guid Bimbo = new("b1b00000-0000-4000-8000-000000000326");
    private static readonly Guid Coelba = new("c0e1ba00-0000-4000-8000-000000000326");

    private const string ColumnsSql =
        """
        SELECT (column_name || ':' || data_type || ':' || is_nullable || ':' || coalesce(column_default, '-'))::text AS "Value"
        FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'OrganizationGrants'
          AND column_name IN ('RequiresScopedRole', 'OrderTypeIds')
        ORDER BY column_name
        """;

    [Fact]
    public async Task Migration_OnADatabaseWithExistingGrants_KeepsEveryOneOrganizationWide_ForEveryOrderType()
    {
        await using var db = await NewContextOnEmptyDatabaseAsync();
        var migrator = db.GetService<IMigrator>();
        var (target, previous) = TargetAndPrevious(db);

        await migrator.MigrateAsync(previous);
        await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO "OrganizationGrants"
                ("Id", "GranteeOrganizationId", "ResourceOrganizationId", "ResourceType", "ScopeId", "AllowedActions", "RequiredRole", "GrantedBy", "GrantedAt", "IsActive")
            VALUES (gen_random_uuid(), {Bimbo}, {Coelba}, 'ServiceOrder', '3550308', '["serviceorders:view"]', 'Supervisor', 'seed', now(), true)
            """);

        await migrator.MigrateAsync(target);

        (await QueryAsync(db, ColumnsSql)).Should().Equal(
            "OrderTypeIds:text:NO:'[]'::text",
            "RequiresScopedRole:boolean:NO:false");
        var grant = (await new EFOrganizationGrantRepository(db, TimeProvider.System).GetActiveGrantsAsync(Bimbo, Coelba))
            .Should().ContainSingle().Subject;
        grant.RequiresScopedRole.Should().BeFalse();
        grant.OrderTypeIds.Should().BeEmpty();
        grant.AllowedActions.Should().Equal("serviceorders:view");
        grant.RequiredRole.Should().Be("Supervisor");
    }

    [Fact]
    public async Task AfterTheMigration_BothFieldsRoundTrip_AndDownDropsThem()
    {
        await using var db = await NewContextOnEmptyDatabaseAsync();
        var migrator = db.GetService<IMigrator>();
        var (_, previous) = TargetAndPrevious(db);
        await db.Database.MigrateAsync();

        db.OrganizationGrants.Add(new OrganizationGrant
        {
            GranteeOrganizationId = Bimbo,
            ResourceOrganizationId = Coelba,
            ResourceType = "ServiceOrder",
            AllowedActions = ["serviceorders:execute"],
            RequiresScopedRole = true,
            OrderTypeIds = ["cut", "reconnect"],
            GrantedBy = "admin@coelba.test",
            GrantedAt = new DateTime(2026, 11, 22, 0, 0, 0, DateTimeKind.Utc),
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var stored = await db.OrganizationGrants.SingleAsync();
        stored.RequiresScopedRole.Should().BeTrue();
        stored.OrderTypeIds.Should().Equal("cut", "reconnect");

        await migrator.MigrateAsync(previous);

        (await QueryAsync(db, ColumnsSql)).Should().BeEmpty();
        (await QueryAsync(db, """SELECT count(*)::text AS "Value" FROM "OrganizationGrants" """)).Should().Equal("1");
    }

    private static (string Target, string Previous) TargetAndPrevious(GeoIdentityDbContext db)
    {
        var chain = db.Database.GetMigrations().ToList();
        var target = chain.Single(id => id.EndsWith("_" + MigrationName, StringComparison.Ordinal));
        return (target, chain[chain.IndexOf(target) - 1]);
    }

    private static Task<List<string>> QueryAsync(GeoIdentityDbContext db, string sql) =>
        db.Database.SqlQueryRaw<string>(sql).ToListAsync();

    private static async Task<GeoIdentityDbContext> NewContextOnEmptyDatabaseAsync()
    {
        var connectionString = new NpgsqlConnectionStringBuilder(await SecurityTestContainerFixture.EnsureStartedAsync())
        {
            Database = $"identity_chain_{Guid.NewGuid():N}",
        }.ConnectionString;

        return new GeoIdentityDbContext(SecurityTestContainerFixture.BuildGeoIdentityOptions(connectionString));
    }
}
