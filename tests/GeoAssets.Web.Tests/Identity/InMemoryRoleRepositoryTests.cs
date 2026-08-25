using FluentAssertions;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Web.Services.Identity;
using GeoAssets.Web.Services.Identity.InMemory;
using Xunit;

namespace GeoAssets.Web.Tests.Identity;

public class InMemoryRoleRepositoryTests
{
    [Fact]
    public async Task GetByIdAsync_PopulatesRolePermissionsFromGrantedPermissions()
    {
        // Regression guard: GrantPermissionAsync only ever touched the flat
        // store.RolePermissions list, never the AppRole.RolePermissions nav property on the
        // stored role instance — without populating it in GetByIdAsync, the identity admin UI
        // (XD01-58) would show every role as having zero granted permissions.
        var store = new WasmIdentityStore();
        var role = new AppRole { Id = Guid.NewGuid(), Name = "Auditor", Description = "desc", IsBuiltIn = false };
        store.Roles.Add(role);
        var sut = new InMemoryRoleRepository(store);
        var permissionId = Guid.NewGuid();

        await sut.GrantPermissionAsync(role.Id, permissionId);
        var fetched = await sut.GetByIdAsync(role.Id);

        fetched.Should().NotBeNull();
        fetched!.RolePermissions.Should().ContainSingle(rp => rp.PermissionId == permissionId && rp.RoleId == role.Id);
    }

    [Fact]
    public async Task GetByIdAsync_NoGrantedPermissions_ReturnsEmptyRolePermissions()
    {
        var store = new WasmIdentityStore();
        var role = new AppRole { Id = Guid.NewGuid(), Name = "Auditor", Description = "desc", IsBuiltIn = false };
        store.Roles.Add(role);
        var sut = new InMemoryRoleRepository(store);

        var fetched = await sut.GetByIdAsync(role.Id);

        fetched.Should().NotBeNull();
        fetched!.RolePermissions.Should().BeEmpty();
    }

    [Fact]
    public async Task GetByIdAsync_AfterRevoke_RolePermissionsNoLongerIncludesIt()
    {
        var store = new WasmIdentityStore();
        var role = new AppRole { Id = Guid.NewGuid(), Name = "Auditor", Description = "desc", IsBuiltIn = false };
        store.Roles.Add(role);
        var sut = new InMemoryRoleRepository(store);
        var permissionId = Guid.NewGuid();
        await sut.GrantPermissionAsync(role.Id, permissionId);

        await sut.RevokePermissionAsync(role.Id, permissionId);
        var fetched = await sut.GetByIdAsync(role.Id);

        fetched!.RolePermissions.Should().BeEmpty();
    }

    [Fact]
    public async Task GetByIdAsync_UnknownId_ReturnsNull()
    {
        var sut = new InMemoryRoleRepository(new WasmIdentityStore());

        var fetched = await sut.GetByIdAsync(Guid.NewGuid());

        fetched.Should().BeNull();
    }
}
