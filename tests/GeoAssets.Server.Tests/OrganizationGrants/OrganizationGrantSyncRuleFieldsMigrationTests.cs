using FluentAssertions;
using GeoAssets.Identity.Authorization.EFCore;
using GeoAssets.Identity.Authorization.EFCore.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace GeoAssets.Server.Tests.OrganizationGrants;

/// <summary>
/// XD01-328 — the <c>AddOrganizationGrantSyncRuleFields</c> migration of the kernel's <see cref="GeoIdentityDbContext"/> chain on
/// PostgreSQL: rows recorded before it keep their place (and read the new fields as their defaults), every existing grant is
/// re-recorded after them with its rule fields, so a consumer's next poll brings each grant up to date; and it reverts.
/// </summary>
[Collection("OrganizationGrantsPostgres")]
public sealed class OrganizationGrantSyncRuleFieldsMigrationTests
{
    private const string MigrationName = "AddOrganizationGrantSyncRuleFields";

    private static readonly Guid Bimbo = new("b1b0b1b0-0000-4000-8000-000000000328");
    private static readonly Guid Coelba = new("c0e1ba00-0000-4000-8000-000000000328");
    private static readonly Guid ScopedGrant = new("10000000-0000-4000-8000-000000000328");
    private static readonly Guid WideGrant = new("20000000-0000-4000-8000-000000000328");

    private static string MigrationId(GeoIdentityDbContext db, int offset = 0)
    {
        var all = db.Database.GetMigrations().ToList();
        return all[all.FindIndex(m => m.EndsWith(MigrationName, StringComparison.Ordinal)) + offset];
    }

    private const string SeedSql = """
        INSERT INTO "OrganizationGrants" ("Id", "GranteeOrganizationId", "ResourceOrganizationId", "ResourceType", "ScopeId", "AllowedActions",
            "RequiredRole", "RequiresScopedRole", "OrderTypeIds", "ExpiresAt", "GrantedBy", "GrantedAt", "IsActive")
        VALUES (@scoped, @bimbo, @coelba, 'ServiceOrder', 'district-7', '["serviceorders:view","serviceorders:execute"]',
                'Contractor C1', true, '["cut"]', NULL, 'admin', '2026-11-01T00:00:00Z', true),
               (@wide, @bimbo, @coelba, NULL, NULL, '["features:read"]', NULL, false, '[]', NULL, 'admin', '2026-11-02T00:00:00Z', false);
        INSERT INTO "OrganizationGrantSyncOutbox" ("GrantId", "GranteeOrganizationId", "ResourceOrganizationId", "ResourceType", "ScopeId", "IsActive", "ExpiresAt", "RecordedAtUtc")
        VALUES (@scoped, @bimbo, @coelba, 'ServiceOrder', 'district-7', true, NULL, '2026-11-01T00:00:00Z');
        """;

    /// <summary>Two grants, and the one entry an older consumer already read, in the shape the tables have before the migration.</summary>
    private static async Task SeedBeforeTheMigrationAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(SeedSql, connection);
        command.Parameters.AddWithValue("scoped", ScopedGrant);
        command.Parameters.AddWithValue("wide", WideGrant);
        command.Parameters.AddWithValue("bimbo", Bimbo);
        command.Parameters.AddWithValue("coelba", Coelba);
        await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Up_KeepsTheOlderEntries_ThenReRecordsEveryGrantWithItsRuleFields_AndDownReverts()
    {
        var connectionString = await OrganizationGrantsPostgres.CreateDatabaseAsync();
        await using var db = OrganizationGrantsPostgres.NewContext(connectionString);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(MigrationId(db, -1));

        await SeedBeforeTheMigrationAsync(connectionString);

        await migrator.MigrateAsync(MigrationId(db));

        await using (var read = OrganizationGrantsPostgres.NewContext(connectionString))
        {
            var feed = await new EFOrganizationGrantRepository(read, TimeProvider.System).GetSyncBatchAsync(0, 100);

            feed.Select(e => e.GrantId).Should().Equal(ScopedGrant, ScopedGrant, WideGrant);
            feed[0].AllowedActions.Should().BeEmpty("an entry recorded before the migration carries none of the new fields");
            feed[0].RequiresScopedRole.Should().BeFalse();
            feed[1].AllowedActions.Should().Equal("serviceorders:view", "serviceorders:execute");
            feed[1].RequiredRole.Should().Be("Contractor C1");
            feed[1].RequiresScopedRole.Should().BeTrue();
            feed[1].OrderTypeIds.Should().Equal("cut");
            feed[1].IsActive.Should().BeTrue();
            feed[2].AllowedActions.Should().Equal("features:read");
            feed[2].IsActive.Should().BeFalse("a revoked grant is re-recorded as revoked");
        }

        await migrator.MigrateAsync(MigrationId(db, -1));
        await using (var reverted = OrganizationGrantsPostgres.NewContext(connectionString))
        {
            (await reverted.Database.SqlQueryRaw<string>(
                """SELECT column_name::text AS "Value" FROM information_schema.columns WHERE table_name = 'OrganizationGrantSyncOutbox' AND column_name IN ('AllowedActions', 'OrderTypeIds', 'RequiredRole', 'RequiresScopedRole')""")
                .ToListAsync()).Should().BeEmpty();
            (await reverted.Database.SqlQueryRaw<string>(
                """SELECT table_name::text AS "Value" FROM information_schema.tables WHERE table_name = 'ScopedRoleAssignmentSyncOutbox'""")
                .ToListAsync()).Should().BeEmpty();
        }

        await migrator.MigrateAsync();
        (await db.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
        db.Database.HasPendingModelChanges().Should().BeFalse();
    }
}
