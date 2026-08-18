using FluentAssertions;
using GeoAssets.Identity.Authentication;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;
using GeoAssets.Identity.Authorization.Services;
using Xunit;

namespace GeoAssets.Identity.Tests.Authorization;

/// <summary>
/// Proves roles are sourced from the external provider's roles claim
/// (<see cref="CurrentUser.AzureRoles"/>) rather than the local <c>UserRole</c> assignment
/// table (XD01-19) — including that the local table's repository methods are never even
/// called by this path, which is exactly what "not the local table" needs to mean.
/// </summary>
public class GeoAuthorizationServiceTests
{
    private sealed class FakeCurrentUserAccessor(CurrentUser? user) : ICurrentUserAccessor
    {
        public CurrentUser? GetCurrentUser() => user;
        public Task<CurrentUser?> GetCurrentUserAsync(CancellationToken ct = default) => Task.FromResult(user);
    }

    private sealed class FakeUserRepository(AppUser? user) : IUserRepository
    {
        public Task<AppUser?> GetByIdAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AppUser?> GetByAzureObjectIdAsync(string oid, CancellationToken ct = default) => Task.FromResult(user);
        public Task<AppUser?> GetByEmailAsync(string email, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<AppUser>> GetAllAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<AppUser>> GetByRoleAsync(string roleName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<AppUser>> GetByOrganizationAsync(Guid organizationId, CancellationToken ct = default) => throw new NotSupportedException();

        // Deliberately throw: GeoAuthorizationService (XD01-19) must no longer source roles
        // or permissions from the local UserRole assignment table at all.
        public Task<IReadOnlyList<AppRole>> GetRolesAsync(Guid userId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<AppPermission>> GetEffectivePermissionsAsync(Guid userId, CancellationToken ct = default) => throw new NotSupportedException();

        public Task AddAsync(AppUser user, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpdateAsync(AppUser user, CancellationToken ct = default) => Task.CompletedTask;
        public Task AssignRoleAsync(Guid userId, Guid roleId, string? assignedBy = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task RemoveRoleAsync(Guid userId, Guid roleId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeUserClaimRepository(IReadOnlyList<UserClaim> claims) : IUserClaimRepository
    {
        public Task<IReadOnlyList<UserClaim>> GetByUserIdAsync(Guid userId, CancellationToken ct = default) => Task.FromResult(claims);
        public Task<IReadOnlyList<UserClaim>> GetByTypeAsync(string claimType, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<UserClaim?> GetAsync(Guid userId, string claimType, CancellationToken ct = default) => throw new NotSupportedException();
        public Task AddAsync(UserClaim claim, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpdateAsync(UserClaim claim, CancellationToken ct = default) => throw new NotSupportedException();
        public Task RemoveAsync(Guid claimId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task RemoveAllAsync(Guid userId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveChangesAsync(CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakePolicyRepository : IPolicyRepository
    {
        public Task<AppPolicy?> GetByIdAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AppPolicy?> GetByNameAsync(string name, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<AppPolicy>> GetAllAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task AddAsync(AppPolicy policy, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpdateAsync(AppPolicy policy, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveChangesAsync(CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakeRoleRepository : IRoleRepository
    {
        private readonly Dictionary<string, AppRole> _rolesByName = [];
        private readonly Dictionary<Guid, List<AppPermission>> _permissionsByRoleId = [];

        public void AddRole(string name, params string[] permissionCodes)
        {
            var role = new AppRole { Name = name };
            _rolesByName[name] = role;
            _permissionsByRoleId[role.Id] = [.. permissionCodes.Select(c => new AppPermission { Code = c })];
        }

        public Task<AppRole?> GetByNameAsync(string name, CancellationToken ct = default)
            => Task.FromResult(_rolesByName.GetValueOrDefault(name));

        public Task<IReadOnlyList<AppPermission>> GetPermissionsAsync(Guid roleId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<AppPermission>>(_permissionsByRoleId.GetValueOrDefault(roleId) ?? []);

        public Task<AppRole?> GetByIdAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<AppRole>> GetAllAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task AddAsync(AppRole role, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpdateAsync(AppRole role, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task GrantPermissionAsync(Guid roleId, Guid permissionId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task RevokePermissionAsync(Guid roleId, Guid permissionId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveChangesAsync(CancellationToken ct = default) => throw new NotSupportedException();
    }

    private static GeoAuthorizationService Sut(
        CurrentUser currentUser, AppUser? provisionedUser, FakeRoleRepository? roleRepository = null,
        IReadOnlyList<UserClaim>? claims = null) =>
        new(
            new FakeCurrentUserAccessor(currentUser),
            new FakeUserRepository(provisionedUser),
            new FakeUserClaimRepository(claims ?? []),
            new FakePolicyRepository(),
            roleRepository ?? new FakeRoleRepository(),
            TimeProvider.System);

    private static AppUser ProvisionedUser(string azureObjectId) => new()
    {
        AzureObjectId = azureObjectId,
        Email         = "a@example.com",
        DisplayName   = "Test",
        CreatedAt     = DateTime.UtcNow,
    };

    [Fact]
    public async Task GetAuthorizationContextAsync_UnprovisionedUser_ReturnsEmptyContext()
    {
        var sut = Sut(new CurrentUser("user-1", "a@example.com", "Ada", ["Supervisor"]), provisionedUser: null);

        var ctx = await sut.GetAuthorizationContextAsync();

        ctx.Roles.Should().BeEmpty();
        ctx.Permissions.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAuthorizationContextAsync_RolesComeFromTokenClaimNotLocalTable()
    {
        // The FakeUserRepository throws if GetRolesAsync is ever called — this would fail
        // immediately if the fix regressed to the old local-table lookup.
        var user = ProvisionedUser("user-1");
        var sut = Sut(new CurrentUser("user-1", "a@example.com", "Ada", ["Supervisor", "FieldTechnician"]), user);

        var ctx = await sut.GetAuthorizationContextAsync();

        ctx.Roles.Should().BeEquivalentTo(["Supervisor", "FieldTechnician"]);
    }

    [Fact]
    public async Task GetAuthorizationContextAsync_NoRolesClaim_ReturnsEmptyRolesAndPermissions()
    {
        // The "drop the JIT default-role grant" half of XD01-19: no external role assignment
        // must mean an empty, not a default, role set.
        var user = ProvisionedUser("user-1");
        var sut = Sut(new CurrentUser("user-1", "a@example.com", "Ada", []), user);

        var ctx = await sut.GetAuthorizationContextAsync();

        ctx.Roles.Should().BeEmpty();
        ctx.Permissions.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAuthorizationContextAsync_ResolvesPermissionsFromLocalRoleDefinitionMatchingClaimRoleName()
    {
        var roleRepo = new FakeRoleRepository();
        roleRepo.AddRole("Supervisor", "serviceorders:assign", "serviceorders:cancel");
        var user = ProvisionedUser("user-1");
        var sut = Sut(new CurrentUser("user-1", "a@example.com", "Ada", ["Supervisor"]), user, roleRepo);

        var ctx = await sut.GetAuthorizationContextAsync();

        ctx.Permissions.Should().BeEquivalentTo(["serviceorders:assign", "serviceorders:cancel"]);
    }

    [Fact]
    public async Task GetAuthorizationContextAsync_ClaimRoleWithNoMatchingLocalRole_ContributesNoPermissionsButStaysListed()
    {
        // A role name the external provider issues but no local AppRole has been created for
        // yet must not throw — it simply grants no permissions until an admin defines it.
        var user = ProvisionedUser("user-1");
        var sut = Sut(new CurrentUser("user-1", "a@example.com", "Ada", ["NotYetDefinedRole"]), user, new FakeRoleRepository());

        var ctx = await sut.GetAuthorizationContextAsync();

        ctx.Roles.Should().BeEquivalentTo(["NotYetDefinedRole"]);
        ctx.Permissions.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAuthorizationContextAsync_MultipleRoles_UnionsPermissionsDeduplicated()
    {
        // Non-leakage in the ordinary sense of "no double counting," not an authz boundary —
        // still worth pinning since Distinct() is easy to drop by accident.
        var roleRepo = new FakeRoleRepository();
        roleRepo.AddRole("Supervisor", "serviceorders:view", "serviceorders:assign");
        roleRepo.AddRole("FieldTechnician", "serviceorders:view", "serviceorders:complete");
        var user = ProvisionedUser("user-1");
        var sut = Sut(new CurrentUser("user-1", "a@example.com", "Ada", ["Supervisor", "FieldTechnician"]), user, roleRepo);

        var ctx = await sut.GetAuthorizationContextAsync();

        ctx.Permissions.Should().BeEquivalentTo(["serviceorders:view", "serviceorders:assign", "serviceorders:complete"]);
    }

    [Fact]
    public async Task GetAuthorizationContextAsync_LoadsClaimsFromClaimRepository()
    {
        var user = ProvisionedUser("user-1");
        var claims = new List<UserClaim> { new() { Type = "zone", Value = "north" } };
        var sut = Sut(new CurrentUser("user-1", "a@example.com", "Ada", []), user, claims: claims);

        var ctx = await sut.GetAuthorizationContextAsync();

        ctx.HasClaim("zone", "north").Should().BeTrue();
    }
}
