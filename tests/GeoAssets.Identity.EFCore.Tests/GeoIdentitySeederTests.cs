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
        (await db.Permissions.CountAsync()).Should().Be(10);
        (await db.Roles.CountAsync()).Should().Be(4);
        (await db.Policies.CountAsync()).Should().Be(5);
    }

    [Fact]
    public async Task SeedAsync_AdministratorRole_IsLinkedToAllTenPermissions()
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

        adminRolePermissionCount.Should().Be(10);
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
        (await db.Permissions.CountAsync()).Should().Be(10);
        (await db.Roles.CountAsync()).Should().Be(4);
        (await db.Policies.CountAsync()).Should().Be(5);
        (await db.RolePermissions.CountAsync(rp => rp.RoleId == GeoIdentitySeeder.AdminRoleId))
            .Should().Be(10);
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
}
