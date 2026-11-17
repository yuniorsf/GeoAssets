using FluentAssertions;
using GeoAssets.Identity.Authorization.EFCore;
using GeoAssets.Identity.Authorization.EFCore.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace GeoAssets.Security.Tests;

/// <summary>
/// Applies the <b>real</b> <see cref="GeoIdentityDbContext"/> migration chain carried by
/// <c>GeoAssets.Server</c> to PostgreSQL and proves the XD01-325 migration
/// (<c>AddUserRoleOrganizationScope</c>: surrogate <c>"UserRoles"."Id"</c> key, nullable
/// <c>"OrganizationId"</c>, one-assignment-per-scope unique indexes) applies over a database that
/// already holds role assignments, leaves each of them effective as a home assignment, and rolls
/// back without turning a scoped assignment into a home one.
///
/// Each fact migrates its own, freshly-named database inside
/// <see cref="SecurityTestContainerFixture"/>'s container, so rolling the migration back here
/// can never disturb the schema the row-level-security facts share.
/// </summary>
public class UserRoleOrganizationScopeMigrationTests
{
    private const string MigrationName = "AddUserRoleOrganizationScope";

    private const string UserId = "aaaaaaaa-0000-0000-0000-000000000001";
    private const string OtherUserId = "aaaaaaaa-0000-0000-0000-000000000002";
    private const string RoleId = "bbbbbbbb-0000-0000-0000-000000000001";
    private const string OtherRoleId = "bbbbbbbb-0000-0000-0000-000000000002";
    private const string HomeOrgId = "cccccccc-0000-0000-0000-000000000001";
    private const string CoelbaId = "cccccccc-0000-0000-0000-000000000002";

    // SqlQueryRaw<string> maps a scalar result set through a column named "Value".
    private const string ColumnsSql =
        """
        SELECT (column_name || ':' || data_type || ':' || is_nullable || ':' || coalesce(column_default, '-'))::text AS "Value"
        FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'UserRoles'
        ORDER BY column_name
        """;

    private const string IndexesSql =
        """
        SELECT indexdef::text AS "Value" FROM pg_indexes
        WHERE schemaname = 'public' AND tablename = 'UserRoles'
        ORDER BY indexname
        """;

    private const string ForeignKeysSql =
        """
        SELECT (conname || ' ' || pg_get_constraintdef(oid))::text AS "Value" FROM pg_constraint
        WHERE conrelid = '"UserRoles"'::regclass AND contype = 'f'
        ORDER BY conname
        """;

    private const string AssignmentsSql =
        """
        SELECT ("UserId"::text || '|' || "RoleId"::text || '|' || coalesce("OrganizationId"::text, 'home') || '|' || coalesce("AssignedBy", '-'))::text AS "Value"
        FROM "UserRoles"
        ORDER BY 1
        """;

    private const string LegacyAssignmentsSql =
        """
        SELECT ("UserId"::text || '|' || "RoleId"::text || '|' || coalesce("AssignedBy", '-'))::text AS "Value"
        FROM "UserRoles"
        ORDER BY 1
        """;

    [Fact]
    public async Task Migration_OnADatabaseWithExistingAssignments_KeepsEveryOneAsAHomeAssignmentWithItsOwnKey()
    {
        await using var db = await NewContextOnEmptyDatabaseAsync();
        var migrator = db.GetService<IMigrator>();
        var (target, previous) = TargetAndPrevious(db);

        // The schema every deployed database has today, with assignments already stored.
        await migrator.MigrateAsync(previous);
        await SeedLegacyAssignmentsAsync(db);

        await migrator.MigrateAsync(target);

        (await QueryAsync(db, AssignmentsSql)).Should().Equal(
            $"{UserId}|{RoleId}|home|seed",
            $"{UserId}|{OtherRoleId}|home|-",
            $"{OtherUserId}|{RoleId}|home|seed");
        (await QueryAsync(db, """SELECT count(DISTINCT "Id")::text AS "Value" FROM "UserRoles" """)).Should().Equal("3");

        // "Effective" as the application reads them: through the repository's home-scoped queries.
        var repository = new EFUserRepository(db, TimeProvider.System);
        (await repository.GetRolesAsync(Guid.Parse(UserId))).Select(r => r.Name)
            .Should().BeEquivalentTo(["LegacyRole", "OtherLegacyRole"]);
        (await repository.GetByRoleAsync("LegacyRole")).Select(u => u.Id.ToString())
            .Should().BeEquivalentTo([UserId, OtherUserId]);
        (await repository.GetScopedRoleAssignmentsAsync(Guid.Parse(UserId))).Should().BeEmpty();
    }

    [Fact]
    public async Task Migration_CreatesTheSurrogateKeyScopeColumnUniqueIndexesAndCascadingForeignKeys()
    {
        await using var db = await NewContextOnEmptyDatabaseAsync();

        await db.Database.MigrateAsync();

        (await db.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
        (await QueryAsync(db, ColumnsSql)).Should().Equal(
            "AssignedAt:timestamp with time zone:NO:-",
            "AssignedBy:character varying:YES:-",
            "Id:uuid:NO:-",
            "OrganizationId:uuid:YES:-",
            "RoleId:uuid:NO:-",
            "UserId:uuid:NO:-");
        (await QueryAsync(db, IndexesSql)).Should().Equal(
            """CREATE INDEX "IX_UserRoles_OrganizationId" ON public."UserRoles" USING btree ("OrganizationId")""",
            """CREATE INDEX "IX_UserRoles_RoleId" ON public."UserRoles" USING btree ("RoleId")""",
            """CREATE UNIQUE INDEX "IX_UserRoles_UserId_RoleId" ON public."UserRoles" USING btree ("UserId", "RoleId") WHERE ("OrganizationId" IS NULL)""",
            """CREATE UNIQUE INDEX "IX_UserRoles_UserId_RoleId_OrganizationId" ON public."UserRoles" USING btree ("UserId", "RoleId", "OrganizationId")""",
            """CREATE UNIQUE INDEX "PK_UserRoles" ON public."UserRoles" USING btree ("Id")""");
        (await QueryAsync(db, ForeignKeysSql)).Should().Equal(
            """FK_UserRoles_Organizations_OrganizationId FOREIGN KEY ("OrganizationId") REFERENCES "Organizations"("Id") ON DELETE CASCADE""",
            """FK_UserRoles_Roles_RoleId FOREIGN KEY ("RoleId") REFERENCES "Roles"("Id") ON DELETE CASCADE""",
            """FK_UserRoles_Users_UserId FOREIGN KEY ("UserId") REFERENCES "Users"("Id") ON DELETE CASCADE""");
    }

    [Fact]
    public async Task MigratedSchema_AllowsOneAssignmentPerScope_AndCascadesFromRoleAndOrganization()
    {
        await using var db = await NewContextOnEmptyDatabaseAsync();
        await db.Database.MigrateAsync();
        await SeedPrincipalsAsync(db);

        // Same user and role: once at home, once on Coelba.
        await InsertAssignmentAsync(db, UserId, RoleId, organizationId: null);
        await InsertAssignmentAsync(db, UserId, RoleId, CoelbaId);
        await InsertAssignmentAsync(db, UserId, OtherRoleId, CoelbaId);

        Func<Task> duplicateHome = async () => await InsertAssignmentAsync(db, UserId, RoleId, organizationId: null);
        Func<Task> duplicateScoped = async () => await InsertAssignmentAsync(db, UserId, RoleId, CoelbaId);
        (await duplicateHome.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
        (await duplicateScoped.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);

        await db.Database.ExecuteSqlRawAsync($"""DELETE FROM "Roles" WHERE "Id" = '{OtherRoleId}'""");
        (await QueryAsync(db, AssignmentsSql)).Should().Equal(
            $"{UserId}|{RoleId}|{CoelbaId}|-",
            $"{UserId}|{RoleId}|home|-");

        await db.Database.ExecuteSqlRawAsync($"""DELETE FROM "Organizations" WHERE "Id" = '{CoelbaId}'""");
        (await QueryAsync(db, AssignmentsSql)).Should().Equal($"{UserId}|{RoleId}|home|-");
    }

    [Fact]
    public async Task Migration_RollsBackDroppingScopedAssignmentsOnly_ThenReapplies()
    {
        await using var db = await NewContextOnEmptyDatabaseAsync();
        var migrator = db.GetService<IMigrator>();
        var (target, previous) = TargetAndPrevious(db);

        await migrator.MigrateAsync(previous);
        await SeedLegacyAssignmentsAsync(db);
        await migrator.MigrateAsync(target);
        // Same (user, role) as an existing home assignment, plus one with no home counterpart:
        // under the old (UserId, RoleId) key the first would collide and the second would be
        // read as a role the user holds at home.
        await InsertAssignmentAsync(db, UserId, RoleId, CoelbaId);
        await InsertAssignmentAsync(db, OtherUserId, OtherRoleId, CoelbaId);

        // Down.
        await migrator.MigrateAsync(previous);
        (await db.Database.GetAppliedMigrationsAsync()).Should().NotContain(target);
        (await QueryAsync(db, LegacyAssignmentsSql)).Should().Equal(
            $"{UserId}|{RoleId}|seed",
            $"{UserId}|{OtherRoleId}|-",
            $"{OtherUserId}|{RoleId}|seed");
        (await QueryAsync(db, IndexesSql)).Should().Equal(
            """CREATE INDEX "IX_UserRoles_RoleId" ON public."UserRoles" USING btree ("RoleId")""",
            """CREATE UNIQUE INDEX "PK_UserRoles" ON public."UserRoles" USING btree ("UserId", "RoleId")""");

        // Up again.
        await db.Database.MigrateAsync();
        (await db.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
        (await QueryAsync(db, AssignmentsSql)).Should().Equal(
            $"{UserId}|{RoleId}|home|seed",
            $"{UserId}|{OtherRoleId}|home|-",
            $"{OtherUserId}|{RoleId}|home|seed");
    }

    private static (string Target, string Previous) TargetAndPrevious(GeoIdentityDbContext db)
    {
        var chain = db.Database.GetMigrations().ToList();
        var target = chain.Single(id => id.EndsWith("_" + MigrationName, StringComparison.Ordinal));
        return (target, chain[chain.IndexOf(target) - 1]);
    }

    private static Task<int> SeedPrincipalsAsync(GeoIdentityDbContext db) =>
        db.Database.ExecuteSqlRawAsync(
            $"""
            INSERT INTO "Organizations" ("Id", "Name", "Slug", "IsActive", "CreatedAt") VALUES
                ('{HomeOrgId}', 'Bimbo', 'legacy-bimbo', true, now()),
                ('{CoelbaId}', 'Coelba', 'legacy-coelba', true, now());
            INSERT INTO "Users" ("Id", "ExternalObjectId", "Email", "DisplayName", "IsActive", "CreatedAt", "OrganizationId") VALUES
                ('{UserId}', 'legacy-oid-1', 'legacy1@example.com', 'Legacy One', true, now(), '{HomeOrgId}'),
                ('{OtherUserId}', 'legacy-oid-2', 'legacy2@example.com', 'Legacy Two', true, now(), '{HomeOrgId}');
            INSERT INTO "Roles" ("Id", "Name", "Description", "IsBuiltIn") VALUES
                ('{RoleId}', 'LegacyRole', '', false),
                ('{OtherRoleId}', 'OtherLegacyRole', '', false);
            """);

    /// <summary>Rows written against the pre-XD01-325 table shape: no "Id", no "OrganizationId".</summary>
    private static async Task SeedLegacyAssignmentsAsync(GeoIdentityDbContext db)
    {
        await SeedPrincipalsAsync(db);
        await db.Database.ExecuteSqlRawAsync(
            $"""
            INSERT INTO "UserRoles" ("UserId", "RoleId", "AssignedAt", "AssignedBy") VALUES
                ('{UserId}', '{RoleId}', now(), 'seed'),
                ('{UserId}', '{OtherRoleId}', now(), NULL),
                ('{OtherUserId}', '{RoleId}', now(), 'seed');
            """);
    }

    private static Task<int> InsertAssignmentAsync(GeoIdentityDbContext db, string userId, string roleId, string? organizationId)
    {
        var (user, role) = (Guid.Parse(userId), Guid.Parse(roleId));
        Guid? organization = organizationId is null ? null : Guid.Parse(organizationId);
        return db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO "UserRoles" ("Id", "UserId", "RoleId", "OrganizationId", "AssignedAt")
            VALUES (gen_random_uuid(), {user}, {role}, {organization}, now())
            """);
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
