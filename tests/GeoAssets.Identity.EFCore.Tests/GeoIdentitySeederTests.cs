using FluentAssertions;
using GeoAssets.Identity.Authorization.EFCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GeoAssets.Identity.EFCore.Tests;

public class GeoIdentitySeederTests
{
    private static GeoIdentityDbContext NewContext(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<GeoIdentityDbContext>()
            .UseSqlite(connection)
            .Options;
        return new GeoIdentityDbContext(options);
    }

    [Fact]
    public async Task SeedAsync_EmptyDatabase_CreatesCanonicalRolesPermissionsPolicies()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var db = NewContext(connection);
        db.Database.EnsureCreated();
        var time = new FakeTimeProvider();

        await GeoIdentitySeeder.SeedAsync(db, time);

        (await db.Organizations.CountAsync()).Should().Be(1);
        (await db.Permissions.CountAsync()).Should().Be(16);
        (await db.Roles.CountAsync()).Should().Be(4);
        (await db.Policies.CountAsync()).Should().Be(5);
    }

    [Fact]
    public async Task SeedAsync_AdministratorRole_IsLinkedToAllSixteenPermissions()
    {
        // Guards the ChangeTracker lookup in AddRoleAsync: permissions and roles are
        // added in the *same* SaveChanges batch, so a naive database-only lookup for
        // the permission (before it's actually persisted) would silently find nothing
        // and leave every role with zero RolePermissions on a fresh database.
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var db = NewContext(connection);
        db.Database.EnsureCreated();

        await GeoIdentitySeeder.SeedAsync(db, new FakeTimeProvider());

        var adminRolePermissionCount = await db.RolePermissions
            .CountAsync(rp => rp.RoleId == GeoIdentitySeeder.AdminRoleId);

        adminRolePermissionCount.Should().Be(16);
    }

    [Fact]
    public async Task SeedAsync_ExistingRoleMissingNewlyAddedGrant_BackfillsIt()
    {
        // XD01-90 regression test: simulates an environment seeded before a permission was
        // added to a role's code-level list — the role and its Permission row both already
        // exist, but the RolePermission linking them doesn't. Without the fix, AddRoleAsync's
        // early-return on "role already exists" would skip the grant loop entirely and this
        // grant would never backfill, exactly as happened in production.
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var db = NewContext(connection);
        db.Database.EnsureCreated();
        await GeoIdentitySeeder.SeedAsync(db, new FakeTimeProvider());

        var usersReadGrant = await db.RolePermissions
            .Include(rp => rp.Permission)
            .SingleAsync(rp => rp.RoleId == GeoIdentitySeeder.AdminRoleId && rp.Permission!.Code == "users:read");
        db.RolePermissions.Remove(usersReadGrant);
        await db.SaveChangesAsync();
        (await db.RolePermissions.CountAsync(rp => rp.RoleId == GeoIdentitySeeder.AdminRoleId)).Should().Be(15);

        await GeoIdentitySeeder.SeedAsync(db, new FakeTimeProvider());

        var adminCodes = await db.RolePermissions
            .Where(rp => rp.RoleId == GeoIdentitySeeder.AdminRoleId)
            .Join(db.Permissions, rp => rp.PermissionId, p => p.Id, (rp, p) => p.Code)
            .ToListAsync();
        adminCodes.Should().Contain("users:read");
        adminCodes.Should().HaveCount(16);
    }

    [Fact]
    public async Task SeedAsync_CalledTwice_IsIdempotent()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var db = NewContext(connection);
        db.Database.EnsureCreated();
        var time = new FakeTimeProvider();

        await GeoIdentitySeeder.SeedAsync(db, time);
        await GeoIdentitySeeder.SeedAsync(db, time);

        (await db.Organizations.CountAsync()).Should().Be(1);
        (await db.Permissions.CountAsync()).Should().Be(16);
        (await db.Roles.CountAsync()).Should().Be(4);
        (await db.Policies.CountAsync()).Should().Be(5);
        (await db.RolePermissions.CountAsync(rp => rp.RoleId == GeoIdentitySeeder.AdminRoleId))
            .Should().Be(16);
    }

    [Fact]
    public async Task SeedAsync_ReadOnlyRole_HasOnlyReadPermissions()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var db = NewContext(connection);
        db.Database.EnsureCreated();

        await GeoIdentitySeeder.SeedAsync(db, new FakeTimeProvider());

        var readOnlyPermissionCodes = await db.RolePermissions
            .Where(rp => rp.RoleId == GeoIdentitySeeder.ReadOnlyRoleId)
            .Join(db.Permissions, rp => rp.PermissionId, p => p.Id, (rp, p) => p.Code)
            .ToListAsync();

        readOnlyPermissionCodes.Should().BeEquivalentTo(["serviceorders:read", "features:read"]);
    }

    private static readonly string[] IdentityAdminPermissionCodes =
    [
        "users:read", "users:edit",
        "roles:read", "roles:edit", "roles:delete",
        "permissions:read"
    ];

    [Fact]
    public async Task SeedAsync_IdentityAdminPermissions_AllSixCodesExist()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var db = NewContext(connection);
        db.Database.EnsureCreated();

        await GeoIdentitySeeder.SeedAsync(db, new FakeTimeProvider());

        var seededCodes = await db.Permissions.Select(p => p.Code).ToListAsync();

        seededCodes.Should().Contain(IdentityAdminPermissionCodes);
    }

    [Fact]
    public async Task SeedAsync_IdentityAdminPermissions_GrantedToAdministratorOnly()
    {
        // Explicit scope-leak guard: XD01-54 Phase 1 restricts the new identity admin
        // permissions to Administrator. If a future edit accidentally copies them onto
        // another built-in role, this test catches it.
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var db = NewContext(connection);
        db.Database.EnsureCreated();

        await GeoIdentitySeeder.SeedAsync(db, new FakeTimeProvider());

        var adminCodes = await db.RolePermissions
            .Where(rp => rp.RoleId == GeoIdentitySeeder.AdminRoleId)
            .Join(db.Permissions, rp => rp.PermissionId, p => p.Id, (rp, p) => p.Code)
            .ToListAsync();
        adminCodes.Should().Contain(IdentityAdminPermissionCodes);

        var nonAdminRoleIds = new[]
        {
            GeoIdentitySeeder.SupervisorRoleId,
            GeoIdentitySeeder.TechnicianRoleId,
            GeoIdentitySeeder.ReadOnlyRoleId
        };
        var nonAdminCodes = await db.RolePermissions
            .Where(rp => nonAdminRoleIds.Contains(rp.RoleId))
            .Join(db.Permissions, rp => rp.PermissionId, p => p.Id, (rp, p) => p.Code)
            .ToListAsync();
        nonAdminCodes.Should().NotContain(IdentityAdminPermissionCodes);
    }
}
