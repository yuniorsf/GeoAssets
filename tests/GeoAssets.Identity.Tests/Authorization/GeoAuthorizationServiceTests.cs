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
        public AppUser? Updated { get; private set; }
        public bool SaveChangesCalled { get; private set; }
        public int GetByExternalObjectIdCallCount { get; private set; }

        /// <summary>What the local table holds as organization-scoped assignments (XD01-325).</summary>
        public IReadOnlyList<UserRole> ScopedAssignments { get; set; } = [];
        public int GetScopedRoleAssignmentsCallCount { get; private set; }
        public CancellationToken LastScopedAssignmentsToken { get; private set; }

        public Task<AppUser?> GetByIdAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AppUser?> GetByExternalObjectIdAsync(string oid, CancellationToken ct = default)
        {
            GetByExternalObjectIdCallCount++;
            return Task.FromResult(_user);
        }
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
            Updated = user;
            _user = user;
            return Task.CompletedTask;
        }
        public Task AssignRoleAsync(Guid userId, Guid roleId, string? assignedBy = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task RemoveRoleAsync(Guid userId, Guid roleId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task AssignRoleOnOrganizationAsync(Guid userId, Guid roleId, Guid organizationId, string? assignedBy = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task RemoveRoleOnOrganizationAsync(Guid userId, Guid roleId, Guid organizationId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<UserRole>> GetScopedRoleAssignmentsAsync(Guid userId, CancellationToken ct = default)
        {
            GetScopedRoleAssignmentsCallCount++;
            LastScopedAssignmentsToken = ct;
            return Task.FromResult<IReadOnlyList<UserRole>>([.. ScopedAssignments.Where(ur => ur.UserId == userId)]);
        }
        public Task<IReadOnlyList<UserRole>> GetRoleAssignmentsOnOrganizationAsync(Guid organizationId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveChangesAsync(CancellationToken ct = default)
        {
            SaveChangesCalled = true;
            return Task.CompletedTask;
        }

        /// <summary>A user a concurrent first request stored before this one could (XD01-475), or null for none.</summary>
        public AppUser? StoredByAConcurrentRequest { get; set; }

        public async Task<AppUser> ProvisionAsync(AppUser user, CancellationToken ct = default)
        {
            if (StoredByAConcurrentRequest is { } stored)
            {
                _user = stored;
                return stored;
            }

            await AddAsync(user, ct);
            await SaveChangesAsync(ct);
            return user;
        }
    }

    private sealed class FakeUserClaimRepository(IReadOnlyList<UserClaim> claims) : IUserClaimRepository
    {
        public int GetByUserIdCallCount { get; private set; }

        public Task<IReadOnlyList<UserClaim>> GetByUserIdAsync(Guid userId, CancellationToken ct = default)
        {
            GetByUserIdCallCount++;
            return Task.FromResult(claims);
        }
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
        private readonly Dictionary<string, AppPolicy> _policiesByName = new(StringComparer.Ordinal);
        public int GetByNameCallCount { get; private set; }

        public void AddPolicy(AppPolicy policy) => _policiesByName[policy.Name] = policy;

        public Task<AppPolicy?> GetByIdAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AppPolicy?> GetByNameAsync(string name, CancellationToken ct = default)
        {
            GetByNameCallCount++;
            return Task.FromResult(_policiesByName.GetValueOrDefault(name));
        }
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
        public int GetByNameCallCount { get; private set; }

        public void AddRole(string name, params string[] permissionCodes)
        {
            var role = new AppRole { Name = name };
            _rolesByName[name] = role;
            _permissionsByRoleId[role.Id] = [.. permissionCodes.Select(c => new AppPermission { Code = c })];
        }

        public Task<AppRole?> GetByNameAsync(string name, CancellationToken ct = default)
        {
            GetByNameCallCount++;
            return Task.FromResult(_rolesByName.GetValueOrDefault(name));
        }

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
        CreatedAt        = TimeProvider.System.GetUtcNow().UtcDateTime,
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
    public async Task GetAuthorizationContextAsync_ProvisioningLostTheRace_UsesTheStoredUser_XD01_475()
    {
        var organization = Guid.NewGuid();
        var stored = ProvisionedUser("user-1");
        stored.OrganizationId = organization;
        var (sut, userRepo) = SutWithUserRepo(new CurrentUser("user-1", "a@example.com", "Ada", []), provisionedUser: null);
        userRepo.StoredByAConcurrentRequest = stored;

        var ctx = await sut.GetAuthorizationContextAsync();

        ctx.User.Should().BeSameAs(stored);
        ctx.User.OrganizationId.Should().Be(organization);
        userRepo.Added.Should().BeNull();
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

    // ── Phase 0.1: Write-on-Read Elimination & Per-Scope Caching ──────────────

    [Fact]
    public async Task GetAuthorizationContextAsync_AlreadyProvisionedUser_DoesNotUpdateOrSaveChanges()
    {
        var user = ProvisionedUser("user-1");
        var (sut, userRepo) = SutWithUserRepo(new CurrentUser("user-1", "a@example.com", "Ada", []), user);

        await sut.GetAuthorizationContextAsync();

        userRepo.Added.Should().BeNull();
        userRepo.Updated.Should().BeNull();
        userRepo.SaveChangesCalled.Should().BeFalse();
    }

    [Fact]
    public async Task GetAuthorizationContextAsync_CalledMultipleTimesInSameScope_QueriesRepositoriesOnlyOnce()
    {
        var user = ProvisionedUser("user-1");
        var userRepo = new FakeUserRepository(user);
        var claimRepo = new FakeUserClaimRepository([new UserClaim { Type = "zone", Value = "north" }]);
        var roleRepo = new FakeRoleRepository();
        roleRepo.AddRole("Supervisor", "serviceorders:assign");

        var sut = new GeoAuthorizationService(
            new FakeCurrentUserAccessor(new CurrentUser("user-1", "a@example.com", "Ada", ["Supervisor"])),
            userRepo,
            claimRepo,
            new FakePolicyRepository(),
            roleRepo,
            TimeProvider.System);

        var ctx1 = await sut.GetAuthorizationContextAsync();
        var ctx2 = await sut.GetAuthorizationContextAsync();
        var hasPerm = await sut.HasPermissionAsync("serviceorders:assign");
        var inRole = await sut.IsInRoleAsync("Supervisor");

        ctx2.Should().BeSameAs(ctx1);
        hasPerm.Should().BeTrue();
        inRole.Should().BeTrue();
        userRepo.GetByExternalObjectIdCallCount.Should().Be(1);
        claimRepo.GetByUserIdCallCount.Should().Be(1);
        roleRepo.GetByNameCallCount.Should().Be(1);
    }

    [Fact]
    public async Task GetAuthorizationContextAsync_UnprovisionedUserAcrossTwoScopes_ProvisionsOnceAndReusesPersistedId()
    {
        var current = new CurrentUser("user-1", "a@example.com", "Ada", []);
        var userRepo = new FakeUserRepository(initialUser: null);

        var scope1 = new GeoAuthorizationService(
            new FakeCurrentUserAccessor(current),
            userRepo,
            new FakeUserClaimRepository([]),
            new FakePolicyRepository(),
            new FakeRoleRepository(),
            TimeProvider.System);
        var scope2 = new GeoAuthorizationService(
            new FakeCurrentUserAccessor(current),
            userRepo,
            new FakeUserClaimRepository([]),
            new FakePolicyRepository(),
            new FakeRoleRepository(),
            TimeProvider.System);

        var ctx1 = await scope1.GetAuthorizationContextAsync();
        var ctx2 = await scope2.GetAuthorizationContextAsync();

        ctx1.User.Id.Should().NotBe(Guid.Empty);
        ctx2.User.Id.Should().Be(ctx1.User.Id);
        userRepo.GetByExternalObjectIdCallCount.Should().Be(2);
        userRepo.Updated.Should().BeNull();
    }

    [Fact]
    public async Task EvaluatePolicyAsync_CalledMultipleTimesForSamePolicyInSameScope_QueriesPolicyRepositoryOnlyOnce()
    {
        var user = ProvisionedUser("user-1");
        var policyRepo = new FakePolicyRepository();
        policyRepo.AddPolicy(new AppPolicy
        {
            Name         = "CanEditFeatures",
            Operator     = PolicyOperator.All,
            Requirements = [new PolicyRequirement { Type = RequirementType.Permission, Value = "features:edit" }]
        });
        var roleRepo = new FakeRoleRepository();
        roleRepo.AddRole("Supervisor", "features:edit");

        var sut = new GeoAuthorizationService(
            new FakeCurrentUserAccessor(new CurrentUser("user-1", "a@example.com", "Ada", ["Supervisor"])),
            new FakeUserRepository(user),
            new FakeUserClaimRepository([]),
            policyRepo,
            roleRepo,
            TimeProvider.System);

        var first = await sut.EvaluatePolicyAsync("CanEditFeatures");
        var second = await sut.EvaluatePolicyAsync("CanEditFeatures");

        first.Should().BeTrue();
        second.Should().BeTrue();
        policyRepo.GetByNameCallCount.Should().Be(1);
    }

    // ── Organization-scoped role assignments (XD01-325) ─────────────────────

    private static readonly Guid HomeOrg = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Coelba = new("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ThirdOrg = new("33333333-3333-3333-3333-333333333333");

    private static UserRole ScopedAssignment(AppUser user, Guid organizationId, string roleName, params string[] permissionCodes)
    {
        var role = new AppRole { Name = roleName };
        role.RolePermissions.AddRange(permissionCodes.Select(code =>
            new RolePermission { Role = role, Permission = new AppPermission { Code = code } }));
        return new UserRole { UserId = user.Id, RoleId = role.Id, OrganizationId = organizationId, Role = role, AssignedAt = default };
    }

    /// <summary>A Supervisor at home (via the token) who also holds "Contractor C1" on Coelba.</summary>
    private static (GeoAuthorizationService Sut, FakeUserRepository UserRepo, FakePolicyRepository Policies) ContractorSut(
        Guid? homeOrganizationId, Func<AppUser, IReadOnlyList<UserRole>> scopedAssignments)
    {
        var user = ProvisionedUser("user-1");
        user.OrganizationId = homeOrganizationId;
        var roleRepo = new FakeRoleRepository();
        roleRepo.AddRole("Supervisor", "serviceorders:read", "serviceorders:assign");
        var userRepo = new FakeUserRepository(user) { ScopedAssignments = scopedAssignments(user) };
        var policies = new FakePolicyRepository();
        var sut = new GeoAuthorizationService(
            new FakeCurrentUserAccessor(new CurrentUser("user-1", "a@example.com", "Ada", ["Supervisor"])),
            userRepo,
            new FakeUserClaimRepository([]),
            policies,
            roleRepo,
            TimeProvider.System);
        return (sut, userRepo, policies);
    }

    [Fact]
    public async Task ScopedAssignment_NeverAppearsInRolesPermissionsOrTheChecksBuiltOnThem()
    {
        // The regression this ticket must not introduce: every existing authorization decision
        // reads Roles/Permissions, so a role held on another organization leaking into them
        // would hand the user that role at home.
        var (sut, _, policies) = ContractorSut(HomeOrg, user =>
            [ScopedAssignment(user, Coelba, "Contractor C1", "serviceorders:execute", "serviceorders:read")]);
        policies.AddPolicy(new AppPolicy
        {
            Name         = "ContractorOnly",
            Operator     = PolicyOperator.Any,
            Requirements =
            [
                new PolicyRequirement { Type = RequirementType.Role, Value = "Contractor C1" },
                new PolicyRequirement { Type = RequirementType.Permission, Value = "serviceorders:execute" },
            ]
        });

        var ctx = await sut.GetAuthorizationContextAsync();

        ctx.Roles.Should().Equal("Supervisor");
        ctx.Permissions.Should().BeEquivalentTo(["serviceorders:read", "serviceorders:assign"]);
        ctx.HasRole("Contractor C1").Should().BeFalse();
        ctx.HasPermission("serviceorders:execute").Should().BeFalse();
        (await sut.IsInRoleAsync("Contractor C1")).Should().BeFalse();
        (await sut.HasPermissionAsync("serviceorders:execute")).Should().BeFalse();
        (await sut.EvaluatePolicyAsync("ContractorOnly")).Should().BeFalse();
        (await sut.IsInRoleAsync("Supervisor")).Should().BeTrue();
        (await sut.HasPermissionAsync("serviceorders:assign")).Should().BeTrue();
    }

    [Fact]
    public async Task RolesOnAndPermissionsOn_AnotherOrganization_ReturnOnlyWhatIsScopedToIt()
    {
        var (sut, _, _) = ContractorSut(HomeOrg, user =>
        [
            ScopedAssignment(user, Coelba, "Contractor C1", "serviceorders:execute"),
            ScopedAssignment(user, ThirdOrg, "Auditor", "reports:export"),
        ]);

        var ctx = await sut.GetAuthorizationContextAsync();

        ctx.RolesOn(Coelba).Should().Equal("Contractor C1");
        ctx.PermissionsOn(Coelba).Should().Equal("serviceorders:execute");
        ctx.HasRoleOn(Coelba, "contractor c1").Should().BeTrue();
        ctx.HasPermissionOn(Coelba, "SERVICEORDERS:EXECUTE").Should().BeTrue();

        // Not the home set, and not what is held on a third organization.
        ctx.HasRoleOn(Coelba, "Supervisor").Should().BeFalse();
        ctx.HasPermissionOn(Coelba, "serviceorders:assign").Should().BeFalse();
        ctx.HasRoleOn(Coelba, "Auditor").Should().BeFalse();
        ctx.HasPermissionOn(Coelba, "reports:export").Should().BeFalse();
        ctx.PermissionsOn(ThirdOrg).Should().Equal("reports:export");
    }

    [Fact]
    public async Task RolesOnAndPermissionsOn_TheUsersOwnOrganization_ReturnTheHomeSet()
    {
        // The stray row scoped to the user's own organization must not widen the home set either.
        var (sut, _, _) = ContractorSut(HomeOrg, user =>
        [
            ScopedAssignment(user, Coelba, "Contractor C1", "serviceorders:execute"),
            ScopedAssignment(user, HomeOrg, "Administrator", "users:manage"),
        ]);

        var ctx = await sut.GetAuthorizationContextAsync();

        ctx.RolesOn(HomeOrg).Should().Equal("Supervisor");
        ctx.PermissionsOn(HomeOrg).Should().BeEquivalentTo(["serviceorders:read", "serviceorders:assign"]);
        ctx.HasRoleOn(HomeOrg, "Supervisor").Should().BeTrue();
        ctx.HasRoleOn(HomeOrg, "Administrator").Should().BeFalse();
        ctx.HasPermissionOn(HomeOrg, "users:manage").Should().BeFalse();
        ctx.HasPermissionOn(HomeOrg, "serviceorders:execute").Should().BeFalse();
        ctx.ScopedRoles.Should().NotContainKey(HomeOrg);
        ctx.ScopedPermissions.Should().NotContainKey(HomeOrg);
    }

    [Fact]
    public async Task RolesOnAndPermissionsOn_AnOrganizationTheUserHoldsNothingOn_AreEmpty()
    {
        var (sut, _, _) = ContractorSut(HomeOrg, user =>
            [ScopedAssignment(user, Coelba, "Contractor C1", "serviceorders:execute")]);

        var ctx = await sut.GetAuthorizationContextAsync();

        ctx.RolesOn(ThirdOrg).Should().BeEmpty();
        ctx.PermissionsOn(ThirdOrg).Should().BeEmpty();
        ctx.HasRoleOn(ThirdOrg, "Contractor C1").Should().BeFalse();
        ctx.HasPermissionOn(ThirdOrg, "serviceorders:read").Should().BeFalse();
    }

    [Fact]
    public async Task PermissionsOn_SeveralRolesOnTheSameOrganization_AreUnionedAndDeduplicated()
    {
        var (sut, _, _) = ContractorSut(HomeOrg, user =>
        [
            ScopedAssignment(user, Coelba, "Contractor C1", "serviceorders:read", "serviceorders:execute"),
            ScopedAssignment(user, Coelba, "Contractor C2", "serviceorders:read", "serviceorders:complete"),
        ]);

        var ctx = await sut.GetAuthorizationContextAsync();

        ctx.RolesOn(Coelba).Should().BeEquivalentTo(["Contractor C1", "Contractor C2"]);
        ctx.PermissionsOn(Coelba).Should().BeEquivalentTo(
            ["serviceorders:read", "serviceorders:execute", "serviceorders:complete"]);
        ctx.PermissionsOn(Coelba).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task SameRoleHeldAtHomeAndOnAnotherOrganization_IsReportedInBothScopes()
    {
        var (sut, _, _) = ContractorSut(HomeOrg, user =>
            [ScopedAssignment(user, Coelba, "Supervisor", "serviceorders:read")]);

        var ctx = await sut.GetAuthorizationContextAsync();

        ctx.HasRole("Supervisor").Should().BeTrue();
        ctx.HasRoleOn(Coelba, "Supervisor").Should().BeTrue();
        // The scoped copy carries the role's local definition, not the home permission set.
        ctx.PermissionsOn(Coelba).Should().Equal("serviceorders:read");
    }

    [Fact]
    public async Task UserWithoutAnOrganization_StillResolvesScopedAssignments_AndHasNoHomeOrganizationToMatch()
    {
        var (sut, _, _) = ContractorSut(homeOrganizationId: null, user =>
            [ScopedAssignment(user, Coelba, "Contractor C1", "serviceorders:execute")]);

        var ctx = await sut.GetAuthorizationContextAsync();

        ctx.RolesOn(Coelba).Should().Equal("Contractor C1");
        ctx.RolesOn(Guid.Empty).Should().BeEmpty();
        ctx.PermissionsOn(Guid.Empty).Should().BeEmpty();
        ctx.Roles.Should().Equal("Supervisor");
    }

    [Fact]
    public async Task ScopedAssignments_AreLoadedOncePerScope_WithTheCallersCancellationToken()
    {
        var (sut, userRepo, _) = ContractorSut(HomeOrg, user =>
            [ScopedAssignment(user, Coelba, "Contractor C1", "serviceorders:execute")]);
        using var cts = new CancellationTokenSource();

        await sut.GetAuthorizationContextAsync(cts.Token);
        await sut.GetAuthorizationContextAsync(cts.Token);
        await sut.HasPermissionAsync("serviceorders:read", cts.Token);

        userRepo.GetScopedRoleAssignmentsCallCount.Should().Be(1);
        userRepo.LastScopedAssignmentsToken.Should().Be(cts.Token);
    }

    [Fact]
    public async Task IsInRoleOnAsyncAndHasPermissionOnAsync_AnswerFromTheResolvedContext()
    {
        var (sut, userRepo, _) = ContractorSut(HomeOrg, user =>
            [ScopedAssignment(user, Coelba, "Contractor C1", "serviceorders:execute")]);
        IGeoAuthorizationService service = sut;

        (await service.IsInRoleOnAsync(Coelba, "Contractor C1")).Should().BeTrue();
        (await service.HasPermissionOnAsync(Coelba, "serviceorders:execute")).Should().BeTrue();
        (await service.IsInRoleOnAsync(Coelba, "Supervisor")).Should().BeFalse();
        (await service.HasPermissionOnAsync(Coelba, "serviceorders:assign")).Should().BeFalse();
        (await service.IsInRoleOnAsync(HomeOrg, "Supervisor")).Should().BeTrue();
        (await service.HasPermissionOnAsync(HomeOrg, "serviceorders:assign")).Should().BeTrue();
        (await service.IsInRoleOnAsync(ThirdOrg, "Contractor C1")).Should().BeFalse();
        (await service.HasPermissionOnAsync(ThirdOrg, "serviceorders:execute")).Should().BeFalse();
        userRepo.GetScopedRoleAssignmentsCallCount.Should().Be(1);
    }

    [Fact]
    public void AuthorizationContext_BuiltWithoutScopedAssignments_HasNoneOnOtherOrganizations()
    {
        // Every pre-existing construction site (REST-backed clients, test fakes) builds the
        // context without the scoped members; that must mean "nothing scoped", not a crash.
        var user = ProvisionedUser("user-1");
        user.OrganizationId = HomeOrg;
        var ctx = new AuthorizationContext { User = user, Roles = ["Supervisor"], Claims = [], Permissions = ["serviceorders:read"] };

        ctx.ScopedRoles.Should().BeEmpty();
        ctx.ScopedPermissions.Should().BeEmpty();
        ctx.RolesOn(Coelba).Should().BeEmpty();
        ctx.PermissionsOn(Coelba).Should().BeEmpty();
        ctx.RolesOn(HomeOrg).Should().Equal("Supervisor");
        ctx.PermissionsOn(HomeOrg).Should().Equal("serviceorders:read");
    }
}
