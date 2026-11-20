using FluentAssertions;
using GeoAssets.Identity.Authorization.EFCore;
using GeoAssets.Identity.Authorization.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace GeoAssets.Server.Tests.OrganizationGrants;

/// <summary>
/// XD01-394 — the <c>EnforceOneActiveOrganizationGrantPerKey</c> migration of the kernel's <see cref="GeoIdentityDbContext"/>
/// chain on PostgreSQL: it aborts, changing nothing, on a database that already holds several active grants of one key (null
/// resource type or scope included), applies on one without — also after the documented clean-up query — and reverts; the
/// model has no pending change against the migrated database.
/// </summary>
[Collection("OrganizationGrantsPostgres")]
public sealed class OrganizationGrantActiveKeyMigrationTests
{
    private const string MigrationName = "EnforceOneActiveOrganizationGrantPerKey";
    private const string IndexName = "UX_OrganizationGrants_ActiveKey";

    private static readonly Guid Grantee = new("a0a0a0a0-0000-4000-8000-000000000394");
    private static readonly Guid Resource = new("b0b0b0b0-0000-4000-8000-000000000394");
    private static readonly DateTime Epoch = new(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);

    // The clean-up query of "Duplicate active organization grants" in deploy/server/README.md.
    private const string CleanUpSql = """
        WITH ranked AS (
            SELECT "Id",
                   row_number() OVER (
                       PARTITION BY "GranteeOrganizationId", "ResourceOrganizationId", "ResourceType", "ScopeId"
                       ORDER BY "GrantedAt", "Id") AS position
              FROM "OrganizationGrants"
             WHERE "IsActive"
        ),
        deactivated AS (
            UPDATE "OrganizationGrants" AS g
               SET "IsActive" = false
              FROM ranked AS r
             WHERE g."Id" = r."Id"
               AND r.position > 1
         RETURNING g."Id", g."GranteeOrganizationId", g."ResourceOrganizationId", g."ResourceType", g."ScopeId", g."ExpiresAt"
        )
        INSERT INTO "OrganizationGrantSyncOutbox"
               ("GrantId", "GranteeOrganizationId", "ResourceOrganizationId", "ResourceType", "ScopeId", "IsActive", "ExpiresAt", "RecordedAtUtc")
        SELECT "Id", "GranteeOrganizationId", "ResourceOrganizationId", "ResourceType", "ScopeId", false, "ExpiresAt", now()
          FROM deactivated
        """;

    [Fact]
    public async Task Migrate_DuplicateActiveGrants_AbortsNamingTheKeysAndGrantsAndChangesNothing()
    {
        var connectionString = await MigratedBeforeAsync();
        var nullKey = new[] { Seed(null, null, 1), Seed(null, null, 2), Seed(null, null, 3) };
        var typedKey = new[] { Seed("ServiceOrder", "3550308", 4), Seed("ServiceOrder", "3550308", 5) };
        var clean = Seed("ServiceOrder", "other", 6);
        await InsertAsync(connectionString, [.. nullKey, .. typedKey, clean]);
        await using var db = OrganizationGrantsPostgres.NewContext(connectionString);

        var act = () => db.GetService<IMigrator>().MigrateAsync();

        var message = (await act.Should().ThrowAsync<PostgresException>()).Which.MessageText;
        message.Should().Contain("Cannot enforce one active organization grant per key: 2 key(s) have several active grants")
            .And.Contain($"(grantee {Grantee}, resource {Resource}, resourceType null, scopeId null): grants {nullKey[0].Id}, {nullKey[1].Id}, {nullKey[2].Id}")
            .And.Contain($"(grantee {Grantee}, resource {Resource}, resourceType 'ServiceOrder', scopeId '3550308'): grants {typedKey[0].Id}, {typedKey[1].Id}")
            .And.Contain("Duplicate active organization grants")
            .And.NotContain(clean.Id.ToString());
        (await IndexExistsAsync(connectionString)).Should().BeFalse();
        (await AppliedMigrationsAsync(connectionString)).Should().NotContain(m => m.EndsWith(MigrationName, StringComparison.Ordinal));
        await using var verify = OrganizationGrantsPostgres.NewContext(connectionString);
        (await verify.OrganizationGrants.AsNoTracking().CountAsync(g => g.IsActive)).Should().Be(6);
    }

    [Fact]
    public async Task Migrate_OnlyInactiveDuplicates_AppliesAndCreatesTheIndex()
    {
        var connectionString = await MigratedBeforeAsync();
        var inactive = new[] { Seed(null, null, 1, isActive: false), Seed(null, null, 2, isActive: false), Seed(null, null, 3) };
        await InsertAsync(connectionString, inactive);
        await using var db = OrganizationGrantsPostgres.NewContext(connectionString);

        await db.GetService<IMigrator>().MigrateAsync();

        (await IndexExistsAsync(connectionString)).Should().BeTrue();
        (await AppliedMigrationsAsync(connectionString)).Should().Contain(m => m.EndsWith(MigrationName, StringComparison.Ordinal));
        await using var verify = OrganizationGrantsPostgres.NewContext(connectionString);
        (await verify.OrganizationGrants.AsNoTracking().CountAsync()).Should().Be(3);
    }

    [Fact]
    public async Task Migrate_EmptyTable_AppliesAndTheIndexRefusesASecondActiveGrantWithNullParts()
    {
        var connectionString = await MigratedBeforeAsync();
        await using var db = OrganizationGrantsPostgres.NewContext(connectionString);
        await db.GetService<IMigrator>().MigrateAsync();
        await InsertAsync(connectionString, [Seed(null, null, 1)]);

        var act = () => InsertAsync(connectionString, [Seed(null, null, 2)]);

        var error = (await act.Should().ThrowAsync<DbUpdateException>()).Which;
        error.InnerException.Should().BeOfType<PostgresException>().Which.ConstraintName.Should().Be(IndexName);
    }

    [Fact]
    public async Task Migrate_AfterTheDocumentedCleanUp_KeepsTheOldestActiveGrantPerKeyAndApplies()
    {
        var connectionString = await MigratedBeforeAsync();
        var oldestNull = Seed(null, null, 1);
        var oldestTyped = Seed("ServiceOrder", "3550308", 1);
        var sameInstantLowerId = Seed("ServiceOrder", "tie", 1);
        var sameInstantHigherId = Seed("ServiceOrder", "tie", 1);
        if (sameInstantLowerId.Id.CompareTo(sameInstantHigherId.Id) > 0) (sameInstantLowerId, sameInstantHigherId) = (sameInstantHigherId, sameInstantLowerId);
        await InsertAsync(
            connectionString,
            [Seed(null, null, 3), oldestNull, Seed(null, null, 2), Seed("ServiceOrder", "3550308", 5), oldestTyped, sameInstantHigherId, sameInstantLowerId]);
        await using var db = OrganizationGrantsPostgres.NewContext(connectionString);
        var guarded = () => db.GetService<IMigrator>().MigrateAsync();
        await guarded.Should().ThrowAsync<PostgresException>();

        await RunCleanUpAsync(connectionString);
        await db.GetService<IMigrator>().MigrateAsync();

        await using var verify = OrganizationGrantsPostgres.NewContext(connectionString);
        var active = await verify.OrganizationGrants.AsNoTracking().Where(g => g.IsActive).ToListAsync();
        active.Select(g => g.Id).Should().BeEquivalentTo([oldestNull.Id, oldestTyped.Id, sameInstantLowerId.Id]);
        (await verify.OrganizationGrants.AsNoTracking().CountAsync()).Should().Be(7);
        var outbox = await new GeoAssets.Identity.Authorization.EFCore.Repositories.EFOrganizationGrantRepository(verify, TimeProvider.System)
            .GetSyncBatchAsync(0, 100);
        outbox.Should().HaveCount(4).And.OnlyContain(e => !e.IsActive);
        (await IndexExistsAsync(connectionString)).Should().BeTrue();
    }

    [Fact]
    public async Task Migrate_DownToThePreviousMigration_DropsTheIndex()
    {
        var connectionString = await OrganizationGrantsPostgres.CreateMigratedDatabaseAsync();
        await using var db = OrganizationGrantsPostgres.NewContext(connectionString);
        (await IndexExistsAsync(connectionString)).Should().BeTrue();

        await db.GetService<IMigrator>().MigrateAsync(PreviousMigrationId(db));

        (await IndexExistsAsync(connectionString)).Should().BeFalse();
        (await AppliedMigrationsAsync(connectionString)).Should().NotContain(m => m.EndsWith(MigrationName, StringComparison.Ordinal));
    }

    [Fact]
    public async Task HasPendingModelChanges_FullyMigratedDatabase_IsFalse()
    {
        var connectionString = await OrganizationGrantsPostgres.CreateMigratedDatabaseAsync();
        await using var db = OrganizationGrantsPostgres.NewContext(connectionString);

        db.Database.HasPendingModelChanges().Should().BeFalse();
        (await db.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
    }

    private static async Task<string> MigratedBeforeAsync()
    {
        var connectionString = await OrganizationGrantsPostgres.CreateDatabaseAsync();
        await using var db = OrganizationGrantsPostgres.NewContext(connectionString);
        await db.GetService<IMigrator>().MigrateAsync(PreviousMigrationId(db));
        return connectionString;
    }

    private static string PreviousMigrationId(GeoIdentityDbContext db)
    {
        var all = db.Database.GetMigrations().ToList();
        var index = all.FindIndex(m => m.EndsWith(MigrationName, StringComparison.Ordinal));
        index.Should().BeGreaterThan(0, "the migration is part of the chain and has a predecessor");
        return all[index - 1];
    }

    private static OrganizationGrant Seed(string? resourceType, string? scopeId, int day, bool isActive = true) => new()
    {
        GranteeOrganizationId = Grantee,
        ResourceOrganizationId = Resource,
        ResourceType = resourceType,
        ScopeId = scopeId,
        AllowedActions = ["ip-billing:read"],
        GrantedBy = "seed",
        GrantedAt = Epoch.AddDays(day),
        IsActive = isActive,
    };

    private static async Task InsertAsync(string connectionString, IEnumerable<OrganizationGrant> grants)
    {
        await using var db = OrganizationGrantsPostgres.NewContext(connectionString);
        db.OrganizationGrants.AddRange(grants);
        await db.SaveChangesAsync();
    }

    private static async Task RunCleanUpAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(CleanUpSql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<bool> IndexExistsAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT count(*) FROM pg_indexes WHERE indexname = @name", connection);
        command.Parameters.AddWithValue("name", IndexName);
        return (long)(await command.ExecuteScalarAsync())! == 1;
    }

    private static async Task<IReadOnlyList<string>> AppliedMigrationsAsync(string connectionString)
    {
        await using var db = OrganizationGrantsPostgres.NewContext(connectionString);
        return [.. await db.Database.GetAppliedMigrationsAsync()];
    }
}
