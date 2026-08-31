using FluentAssertions;
using GeoAssets.Identity.Authentication;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;
using GeoAssets.Identity.Authorization.Services;
using Xunit;

namespace GeoAssets.Identity.Tests.Authorization;

/// <summary>
/// Proves roles are sourced from the external provider's roles claim
/// (<see cref="CurrentUser.ExternalRoles"/>) rather than the local <c>UserRole</c> assignment
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

    /// <summary>Mutable spy: <see cref="AddAsync"/> both records what was added (matching the
    /// <c>FakeAdminUserRepository</c>/<c>FakePendingInvitationRepository</c> idiom used
    /// elsewhere) and updates what subsequent <see cref="GetByExternalObjectIdAsync"/> calls
    /// return, so a test can prove a JIT-provisioned user is actually found again on a second
    /// call (XD01-88) — not just that <c>AddAsync</c> was called once.</summary>
    private sealed class FakeUserRepository(AppUser? initialUser) : IUserRepository
    {
        private AppUser? _user = initialUser;

        public AppUser? Added { get; private set; }
        public bool SaveChangesCalled { get; private set; }

        public Task<AppUser?> GetByIdAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AppUser?> GetByExternalObjectIdAsync(string oid, CancellationToken ct = default) => Task.FromResult(_user);
        public Task<AppUser?> GetByEmailAsync(string email, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<AppUser>> GetAllAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<AppUser>> GetByRoleAsync(string roleName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<AppUser>> GetByOrganizationAsync(Guid organizationId, CancellationToken ct = default) => throw new NotSupportedException();

        // Deliberately throw: GeoAuthorizationService (XD01-19) must no longer source roles
        // or permissions from the local UserRole assignment table at all.
        public Task<IReadOnlyList<AppRole>> GetRolesAsync(Guid userId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<AppPermission>> GetEffectivePermissionsAsync(Guid userId, CancellationToken ct = default) => throw new NotSupportedException();

        public Task AddAsync(AppUser user, CancellationToken ct = default)
        {
            Added = user;
            _user = user;
            return Task.CompletedTask;
        }
        public Task UpdateAsync(AppUser user, CancellationToken ct = default)
        {
            _user = user;
            return Task.CompletedTask;
        }
        public Task AssignRoleAsync(Guid userId, Guid roleId, string? assignedBy = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task RemoveRoleAsync(Guid userId, Guid roleId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveChangesAsync(CancellationToken ct = default)
        {
            SaveChangesCalled = true;
            return Task.CompletedTask;
        }
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

    /// <summary>Like <see cref="Sut"/>, but also hands back the <see cref="FakeUserRepository"/>
    /// spy — needed by the XD01-88 JIT-provisioning tests below to inspect what got persisted.</summary>
    private static (GeoAuthorizationService Sut, FakeUserRepository UserRepo) SutWithUserRepo(
        CurrentUser currentUser, AppUser? provisionedUser, FakeRoleRepository? roleRepository = null,
        IReadOnlyList<UserClaim>? claims = null)
    {
        var userRepo = new FakeUserRepository(provisionedUser);
        var sut = new GeoAuthorizationService(
            new FakeCurrentUserAccessor(currentUser),
            userRepo,
            new FakeUserClaimRepository(claims ?? []),
            new FakePolicyRepository(),
            roleRepository ?? new FakeRoleRepository(),
            TimeProvider.System);
        return (sut, userRepo);
    }

    private static AppUser ProvisionedUser(string externalObjectId) => new()
    {
        ExternalObjectId = externalObjectId,
        Email            = "a@example.com",
        DisplayName      = "Test",
        CreatedAt        = DateTime.UtcNow,
    };

    // ── JIT provisioning (XD01-88) ──────────────────────────────────────────

    [Fact]
    public async Task GetAuthorizationContextAsync_UnprovisionedUser_PersistsNewAppUserAndSavesChanges()
    {
        var (sut, userRepo) = SutWithUserRepo(new CurrentUser("user-1", "a@example.com", "Ada", []), provisionedUser: null);

        await sut.GetAuthorizationContextAsync();

        userRepo.Added.Should().NotBeNull();
        userRepo.Added!.ExternalObjectId.Should().Be("user-1");
        userRepo.Added.Email.Should().Be("a@example.com");
        userRepo.Added.DisplayName.Should().Be("Ada");
        userRepo.SaveChangesCalled.Should().BeTrue();
    }

    [Fact]
    public async Task GetAuthorizationContextAsync_UnprovisionedUser_OrganizationIdStaysNull()
    {
        var (sut, _) = SutWithUserRepo(new CurrentUser("user-1", "a@example.com", "Ada", []), provisionedUser: null);

        var ctx = await sut.GetAuthorizationContextAsync();

        ctx.User.OrganizationId.Should().BeNull();
    }

    [Fact]
    public async Task GetAuthorizationContextAsync_UnprovisionedUser_RolesAndPermissionsResolveOnTheSameFirstCall()
    {
        // Before the fix, a brand-new caller's first request always reported an empty
        // Roles/Permissions set (hardcoded on the early-return) even though their token already
        // carried an ExternalRoles claim, forcing a second round-trip before real permissions
        // applied. Now that provisioning falls through into the normal resolution path instead
        // of early-returning, this should just work on the first call.
        var roleRepo = new FakeRoleRepository();
        roleRepo.AddRole("Supervisor", "serviceorders:assign");
        var (sut, _) = SutWithUserRepo(
            new CurrentUser("user-1", "a@example.com", "Ada", ["Supervisor"]), provisionedUser: null, roleRepo);

        var ctx = await sut.GetAuthorizationContextAsync();

        ctx.Roles.Should().BeEquivalentTo(["Supervisor"]);
        ctx.Permissions.Should().BeEquivalentTo(["serviceorders:assign"]);
    }

    [Fact]
    public async Task GetAuthorizationContextAsync_CalledTwiceForSameUnprovisionedCaller_ReturnsTheSamePersistedUserId()
    {
        // The regression this ticket exists for: before the fix, GetAuthorizationContextAsync
        // returned a fresh, never-persisted Guid on every call for an unprovisioned caller — any
        // write keyed on ctx.User.Id (e.g. POST /invitations, POST /userclaims) referenced a
        // phantom id a second call couldn't even find again, orphaning the write. This test
        // would fail without the fix: ctx2.User.Id would differ from ctx1.User.Id.
        var (sut, _) = SutWithUserRepo(new CurrentUser("user-1", "a@example.com", "Ada", []), provisionedUser: null);

        var ctx1 = await sut.GetAuthorizationContextAsync();
        var ctx2 = await sut.GetAuthorizationContextAsync();

        ctx1.User.Id.Should().NotBe(Guid.Empty);
        ctx2.User.Id.Should().Be(ctx1.User.Id);
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
