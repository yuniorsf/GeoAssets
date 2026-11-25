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
/// PostgreSQL and proves the XD01-411 migration (<c>AddOrganizationGrantSource</c>) marks every grant that already exists
/// as provisioned by a vertical host (<c>"Source"</c> <c>'service'</c>, so it stays read-only to administrators), that an
/// administrator-owned grant round-trips, and that it rolls back.
///
/// Each fact migrates its own, freshly-named database inside <see cref="SecurityTestContainerFixture"/>'s container.
/// </summary>
public class OrganizationGrantSourceMigrationTests
{
    private const string MigrationName = "AddOrganizationGrantSource";

    private static readonly Guid Bimbo = new("b1b00000-0000-4000-8000-000000000411");
    private static readonly Guid Coelba = new("c0e1ba00-0000-4000-8000-000000000411");

    private const string ColumnSql =
        """
        SELECT (column_name || ':' || data_type || ':' || is_nullable)::text AS "Value"
        FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'OrganizationGrants' AND column_name = 'Source'
        """;

    [Fact]
    public async Task Migration_OnADatabaseWithExistingGrants_MarksEveryOneAsServiceOwned()
    {
        await using var db = await NewContextOnEmptyDatabaseAsync();
        var migrator = db.GetService<IMigrator>();
        var (target, previous) = TargetAndPrevious(db);

        await migrator.MigrateAsync(previous);
        await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO "OrganizationGrants"
                ("Id", "GranteeOrganizationId", "ResourceOrganizationId", "ResourceType", "ScopeId", "AllowedActions", "RequiredRole",
                 "GrantedBy", "GrantedAt", "IsActive", "RequiresScopedRole", "OrderTypeIds")
            VALUES (gen_random_uuid(), {Bimbo}, {Coelba}, NULL, '2927408', '["features:read"]', NULL, 'ipbilling-sync', now(), true, false, '[]')
            """);

        await migrator.MigrateAsync(target);

        (await QueryAsync(db, ColumnSql)).Should().Equal("Source:character varying:NO");
        var grant = (await new EFOrganizationGrantRepository(db, TimeProvider.System).GetActiveGrantsAsync(Bimbo, Coelba))
            .Should().ContainSingle().Subject;
        grant.Source.Should().Be(OrganizationGrantSources.Service);
    }

    [Fact]
    public async Task AfterTheMigration_AnAdministratorsGrantRoundTrips_AndDownDropsTheColumn()
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
            AllowedActions = ["serviceorders:read"],
            GrantedBy = "admin@coelba.test",
            GrantedAt = new DateTime(2026, 11, 25, 0, 0, 0, DateTimeKind.Utc),
            Source = OrganizationGrantSources.Administrator,
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        (await db.OrganizationGrants.SingleAsync()).Source.Should().Be(OrganizationGrantSources.Administrator);

        await migrator.MigrateAsync(previous);

        (await QueryAsync(db, ColumnSql)).Should().BeEmpty();
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
