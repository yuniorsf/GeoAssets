using FluentAssertions;
using GeoAssets.Identity.Authorization.EFCore;
using GeoAssets.Identity.Authorization.EFCore.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GeoAssets.Identity.EFCore.Tests;

public class EFRoleRepositoryTests
{
    private static GeoIdentityDbContext NewContext(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<GeoIdentityDbContext>()
            .UseSqlite(connection)
            .Options;
        return new GeoIdentityDbContext(options);
    }

    [Fact]
    public async Task GetPermissionsByRoleNamesAsync_EmptyRoleNames_ReturnsEmptyWithoutError()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var db = NewContext(connection);
        await db.Database.EnsureCreatedAsync();
        await GeoIdentitySeeder.SeedAsync(db, new FakeTimeProvider());

        var sut = new EFRoleRepository(db);

        var permissions = await sut.GetPermissionsByRoleNamesAsync([]);

        permissions.Should().BeEmpty();
    }

    [Fact]
    public async Task GetPermissionsByRoleNamesAsync_MultipleRoles_ReturnsDistinctUnionOfPermissions()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var db = NewContext(connection);
        await db.Database.EnsureCreatedAsync();
        await GeoIdentitySeeder.SeedAsync(db, new FakeTimeProvider());

        var sut = new EFRoleRepository(db);

        var permissions = await sut.GetPermissionsByRoleNamesAsync(["FieldTechnician", "ReadOnly", "UnknownRole"]);
        var codes = permissions.Select(p => p.Code).ToList();

        codes.Should().BeEquivalentTo([
            "serviceorders:read",
            "serviceorders:complete",
            "features:read",
            "features:edit",
            "projects:read"
        ]);
        codes.Should().OnlyHaveUniqueItems();
    }
}

