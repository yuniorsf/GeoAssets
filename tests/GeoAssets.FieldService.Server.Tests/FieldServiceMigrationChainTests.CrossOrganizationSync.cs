using FluentAssertions;
using GeoAssets.Workflow.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace GeoAssets.FieldService.Server.Tests;

/// <summary>
/// XD01-328 on the real FieldService migration chain: the mirrors' new shape, and the row-level-security behaviour of the
/// grant-aware cross-organization read path (<see cref="EFServiceOrderRepository.GetOfOtherOrganizationsAsync"/>) as a
/// non-owner role — the explicit path asks past the tenant query filter, and the policies still decide every row.
/// </summary>
public partial class FieldServiceMigrationChainTests
{
    private const string CrossOrganizationSyncMigration = "AddCrossOrganizationSyncMirrors";

    private const string GrantMirrorRuleColumnsSql =
        """
        SELECT (column_name || ':' || data_type || ':' || is_nullable || ':' || coalesce(column_default, '-'))::text AS "Value"
        FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'OrganizationGrantMirror'
          AND column_name IN ('AllowedActionsJson', 'RequiredRole', 'RequiresScopedRole', 'OrderTypeIdsJson')
        ORDER BY column_name
        """;

    [Fact]
    public async Task CrossOrganizationSyncMigration_KeepsMirroredGrants_AddsTheRuleFieldsAndTheScopedRoleMirror_ThenRollsBack()
    {
        await using var db = await NewContextOnEmptyDatabaseAsync();
        var migrator = db.GetService<IMigrator>();
        var target = MigrationId(db, CrossOrganizationSyncMigration);
        var chain = db.Database.GetMigrations().ToList();
        var previous = chain[chain.IndexOf(target) - 1];

        await migrator.MigrateAsync(previous);
        await using (var owner = await OpenAsync(db))
            await InsertMirroredGrantAsync(owner, isActive: true, isExpired: false, resourceType: "ServiceOrder", scopeId: null);

        await migrator.MigrateAsync(target);

        (await QueryAsync(db, GrantMirrorRuleColumnsSql)).Should().Equal(
            "AllowedActionsJson:text:NO:'[]'::text",
            "OrderTypeIdsJson:text:NO:'[]'::text",
            "RequiredRole:character varying:YES:-",
            "RequiresScopedRole:boolean:NO:false");
        (await QueryAsync(db, """SELECT ("AllowedActionsJson" || '|' || "OrderTypeIdsJson" || '|' || "RequiresScopedRole"::text)::text AS "Value" FROM "OrganizationGrantMirror" """))
            .Should().Equal(["[]|[]|false"], because: "a grant mirrored before the rule fields existed opens nothing until the worker rewrites it");
        (await QueryAsync(db, """SELECT table_name::text AS "Value" FROM information_schema.tables WHERE table_name LIKE 'ScopedRoleAssignment%' ORDER BY 1"""))
            .Should().Equal("ScopedRoleAssignmentMirror", "ScopedRoleAssignmentSyncCursor");

        await migrator.MigrateAsync(previous);
        (await QueryAsync(db, GrantMirrorRuleColumnsSql)).Should().BeEmpty();
        (await QueryAsync(db, """SELECT table_name::text AS "Value" FROM information_schema.tables WHERE table_name LIKE 'ScopedRoleAssignment%'""")).Should().BeEmpty();
        (await QueryAsync(db, """SELECT count(*)::text AS "Value" FROM "OrganizationGrantMirror" """)).Should().Equal("1");
    }

    /// <summary>
    /// The grantee's session, through <see cref="EFServiceOrderRepository.GetOfOtherOrganizationsAsync"/> on the tenant-scoped
    /// role's own connection — the same session variable the tenant interceptors set.
    /// </summary>
    private static async Task<List<string>> CrossOrganizationReadAsync(NpgsqlConnection scoped, Guid session, params Guid[] asked)
    {
        await SetOrganizationAsync(scoped, session);
        await using var context = new ServiceOrderDbContext(
            new DbContextOptionsBuilder<ServiceOrderDbContext>().UseNpgsql(scoped).Options, new FixedTenantContext(session));
        var orders = await new EFServiceOrderRepository(context).GetOfOtherOrganizationsAsync(asked);
        return [.. orders.Select(o => o.Id).Order(StringComparer.Ordinal)];
    }

    [Fact]
    public async Task CrossOrganizationReadPath_AsANonOwnerRole_ReturnsWhatTheMirroredGrantOpens_AndNothingElseItAsksFor()
    {
        await using var db = await NewContextOnEmptyDatabaseAsync();
        await db.Database.MigrateAsync();
        await using (var owner = await OpenAsync(db))
        {
            await InsertOrderAsync(owner, "ord-a-in-scope", OwnerOrganization, """{"scopeId":"district-7"}""");
            await InsertOrderAsync(owner, "ord-a-out-of-scope", OwnerOrganization, """{"scopeId":"district-9"}""");
            await InsertOrderAsync(owner, "ord-b", StrangerOrganization);
            await InsertOrderAsync(owner, "ord-grantee-own", GranteeOrganization);
            await InsertMirroredGrantAsync(owner, isActive: true, isExpired: false, resourceType: "ServiceOrder", scopeId: "district-7");
        }

        await using var scoped = await OpenAsTenantScopedRoleAsync(db);

        (await CrossOrganizationReadAsync(scoped, GranteeOrganization, OwnerOrganization, StrangerOrganization))
            .Should().Equal(["ord-a-in-scope"], because: "the policy, not the query, refuses the stranger's order and the order outside the grant's scope");
        (await CrossOrganizationReadAsync(scoped, StrangerOrganization, OwnerOrganization))
            .Should().BeEmpty("the grant names another grantee");
    }

    [Fact]
    public async Task CrossOrganizationReadPath_AsANonOwnerRole_IsClosedByTheNextMirrorUpdate()
    {
        await using var db = await NewContextOnEmptyDatabaseAsync();
        await db.Database.MigrateAsync();
        await using (var owner = await OpenAsync(db))
        {
            await InsertOrderAsync(owner, "ord-a", OwnerOrganization);
            await InsertMirroredGrantAsync(owner, isActive: true, isExpired: false, resourceType: null, scopeId: null);
        }

        await using var scoped = await OpenAsTenantScopedRoleAsync(db);
        (await CrossOrganizationReadAsync(scoped, GranteeOrganization, OwnerOrganization)).Should().Equal("ord-a");

        // The sync worker applying the revocation it was fed.
        await using (var owner = await OpenAsync(db))
            await ExecuteAsync(owner, """UPDATE "OrganizationGrantMirror" SET "IsActive" = false""");

        (await CrossOrganizationReadAsync(scoped, GranteeOrganization, OwnerOrganization)).Should().BeEmpty();
    }

    [Fact]
    public async Task CrossOrganizationReadPath_AsANonOwnerRole_LeavesTheContextsFilteredReadsOwnOrganizationOnly()
    {
        await using var db = await NewContextOnEmptyDatabaseAsync();
        await db.Database.MigrateAsync();
        await using (var owner = await OpenAsync(db))
        {
            await InsertOrderAsync(owner, "ord-a", OwnerOrganization);
            await InsertOrderAsync(owner, "ord-grantee-own", GranteeOrganization);
            await InsertMirroredGrantAsync(owner, isActive: true, isExpired: false, resourceType: null, scopeId: null);
        }

        await using var scoped = await OpenAsTenantScopedRoleAsync(db);
        await SetOrganizationAsync(scoped, GranteeOrganization);
        await using var context = new ServiceOrderDbContext(
            new DbContextOptionsBuilder<ServiceOrderDbContext>().UseNpgsql(scoped).Options, new FixedTenantContext(GranteeOrganization));
        var repo = new EFServiceOrderRepository(context);

        (await repo.GetOfOtherOrganizationsAsync([OwnerOrganization])).Select(o => o.Id).Should().Equal("ord-a");
        (await repo.GetAllAsync()).Select(o => o.Id).Should().Equal(["ord-grantee-own"], because: "the tenant query filter stays on, though the policy would let the grantee read the granted order");
    }
}
