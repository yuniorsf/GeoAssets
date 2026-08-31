using FluentAssertions;
using GeoAssets.Web.Services.Identity;
using Xunit;

namespace GeoAssets.Web.Tests.Identity;

/// <summary>
/// Covers the same XD01-90 backfill fix as <c>GeoIdentitySeederTests</c>
/// (<c>GeoAssets.Identity.EFCore.Tests</c>) — this class mirrors that Server/EF seeder for the
/// WASM in-memory store. Practical impact here is much smaller (<c>WasmIdentityStore</c> never
/// persists across restarts, so a role can only already "exist" if <see cref="IdentitySeeder.Seed"/>
/// somehow ran twice against the same store), but the bug shape was identical and worth the
/// same regression coverage.
/// </summary>
public class IdentitySeederTests
{
    [Fact]
    public void Seed_ExistingRoleMissingGrant_BackfillsIt()
    {
        var store = new WasmIdentityStore();
        var seeder = new IdentitySeeder(store, TimeProvider.System);
        seeder.Seed();

        var usersReadPermission = store.Permissions.Single(p => p.Code == "users:read");
        store.RolePermissions.RemoveAll(rp =>
            rp.RoleId == IdentitySeeder.AdminRoleId && rp.PermissionId == usersReadPermission.Id);

        seeder.Seed();

        var adminCodes = store.RolePermissions
            .Where(rp => rp.RoleId == IdentitySeeder.AdminRoleId)
            .Join(store.Permissions, rp => rp.PermissionId, p => p.Id, (rp, p) => p.Code)
            .ToList();
        adminCodes.Should().Contain("users:read");
        adminCodes.Should().HaveCount(16);
    }

    [Fact]
    public void Seed_CalledTwice_IsIdempotent()
    {
        var store = new WasmIdentityStore();
        var seeder = new IdentitySeeder(store, TimeProvider.System);

        seeder.Seed();
        seeder.Seed();

        store.RolePermissions.Count(rp => rp.RoleId == IdentitySeeder.AdminRoleId).Should().Be(16);
        store.Roles.Should().HaveCount(4);
    }
}
