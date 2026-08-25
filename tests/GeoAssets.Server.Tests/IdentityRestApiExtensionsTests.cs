using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using FluentAssertions;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;
using GeoAssets.Identity.Authorization.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace GeoAssets.Server.Tests;

public class IdentityRestApiExtensionsTests
{
    private sealed class FakeAuthorizationService(AuthorizationContext context) : IGeoAuthorizationService
    {
        public Task<bool> IsInRoleAsync(string roleName, CancellationToken ct = default) => Task.FromResult(context.HasRole(roleName));
        public Task<bool> HasClaimAsync(string claimType, string? claimValue = null, CancellationToken ct = default) => Task.FromResult(context.HasClaim(claimType, claimValue));
        public Task<bool> HasPermissionAsync(string permissionCode, CancellationToken ct = default) => Task.FromResult(context.HasPermission(permissionCode));
        public Task<bool> EvaluatePolicyAsync(string policyName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> EvaluatePolicyAsync(AppPolicy policy, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AuthorizationContext> GetAuthorizationContextAsync(CancellationToken ct = default) => Task.FromResult(context);
    }

    private sealed class FakePolicyRepository(IReadOnlyList<AppPolicy> policies) : IPolicyRepository
    {
        public Task<AppPolicy?> GetByIdAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AppPolicy?> GetByNameAsync(string name, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<AppPolicy>> GetAllAsync(CancellationToken ct = default) => Task.FromResult(policies);
        public Task AddAsync(AppPolicy policy, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpdateAsync(AppPolicy policy, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveChangesAsync(CancellationToken ct = default) => throw new NotSupportedException();
    }

    private static async Task<TestServer> BuildServerAsync(IGeoAuthorizationService authService, IPolicyRepository policyRepo)
    {
        var host = await new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddSingleton(authService);
                    services.AddSingleton(policyRepo);
                    // MapIdentityApi() maps the XD01-56 admin routes in the same group as
                    // /me and /policies — endpoint metadata for the whole group is built
                    // lazily on first request, so these must be resolvable even though the
                    // tests using this host only ever call /me or /policies.
                    services.AddSingleton<IUserRepository>(
                        new FakeAdminUserRepository(new Dictionary<Guid, AppUser>(), new Dictionary<Guid, List<AppRole>>()));
                    services.AddSingleton<IRoleRepository>(
                        new FakeAdminRoleRepository(new Dictionary<Guid, AppRole>(), new Dictionary<Guid, List<AppPermission>>()));
                    services.AddSingleton<IPermissionRepository>(new FakeAdminPermissionRepository([]));
                    services.AddSingleton<IRoleAssignmentProvider>(new NullRoleAssignmentProvider());
                    // Same reasoning — the XD01-69 invitations routes are in the same group.
                    services.AddSingleton<IPendingInvitationRepository>(new NeverCalledPendingInvitationRepository());
                    services.AddSingleton<IUserInvitationProvider>(new NullUserInvitationProvider());
                    services.AddSingleton<IInvitationEmailSender>(new NullInvitationEmailSender());
                    // Same reasoning — the XD01-87 userclaims routes are in the same group.
                    services.AddSingleton<IUserClaimRepository>(new FakeUserClaimRepository(new Dictionary<Guid, List<UserClaim>>()));
                });
                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapIdentityApi());
                });
            })
            .StartAsync();

        return host.GetTestServer();
    }

    private static AppUser NewUser(string email = "user@example.com", string externalObjectId = "") => new()
    {
        Id               = Guid.NewGuid(),
        Email            = email,
        DisplayName      = "Test User",
        CreatedAt        = DateTime.UtcNow,
        ExternalObjectId = externalObjectId,
    };

    [Fact]
    public async Task Me_ReturnsFlattenedAuthorizationContext()
    {
        var user = NewUser();
        var ctx = new AuthorizationContext
        {
            User        = user,
            Roles       = ["Administrator"],
            Claims      = [new UserClaim { UserId = user.Id, Type = "zone", Value = "north" }],
            Permissions = ["features:read", "features:edit"]
        };

        using var server = await BuildServerAsync(new FakeAuthorizationService(ctx), new FakePolicyRepository([]));
        using var client = server.CreateClient();

        var dto = await client.GetFromJsonAsync<AuthorizationContextDto>("/api/identity/me");

        dto.Should().NotBeNull();
        dto!.Id.Should().Be(user.Id);
        dto.Email.Should().Be("user@example.com");
        dto.Roles.Should().ContainSingle().Which.Should().Be("Administrator");
        dto.Claims.Should().ContainSingle(c => c.Type == "zone" && c.Value == "north");
        dto.Permissions.Should().BeEquivalentTo(["features:read", "features:edit"]);
    }

    [Fact]
    public async Task Me_UserWithEfRelationshipFixupCycle_DoesNotThrowSerializing()
    {
        // Simulates what EF Core's automatic relationship fixup produces after
        // .Include(u => u.UserRoles).ThenInclude(ur => ur.Role): AppUser -> UserRoles ->
        // UserRole -> User (back to the same AppUser instance). Returning the raw
        // AuthorizationContext/AppUser directly (instead of mapping to
        // AuthorizationContextDto) would make System.Text.Json throw on this cycle.
        var user = NewUser();
        var role = new AppRole { Id = Guid.NewGuid(), Name = "Administrator" };
        var userRole = new UserRole { UserId = user.Id, RoleId = role.Id, AssignedAt = DateTime.UtcNow, User = user, Role = role };
        user.UserRoles.Add(userRole);
        role.UserRoles.Add(userRole);

        var ctx = new AuthorizationContext { User = user, Roles = ["Administrator"], Claims = [], Permissions = [] };

        using var server = await BuildServerAsync(new FakeAuthorizationService(ctx), new FakePolicyRepository([]));
        using var client = server.CreateClient();

        var response = await client.GetAsync("/api/identity/me");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Policies_ReturnsFlattenedPoliciesWithRequirements()
    {
        var policy = new AppPolicy
        {
            Id          = Guid.NewGuid(),
            Name        = "CanEditFeatures",
            Description = "Puede editar activos GIS",
            Operator    = PolicyOperator.All,
            Requirements = [new PolicyRequirement { Type = RequirementType.Permission, Value = "features:edit" }]
        };
        // Simulates EF's Include(p => p.Requirements) fixup: PolicyRequirement.Policy back-reference.
        policy.Requirements[0].Policy = policy;

        using var server = await BuildServerAsync(
            new FakeAuthorizationService(new AuthorizationContext { User = NewUser(), Roles = [], Claims = [], Permissions = [] }),
            new FakePolicyRepository([policy]));
        using var client = server.CreateClient();

        var dtos = await client.GetFromJsonAsync<List<PolicyDto>>("/api/identity/policies");

        dtos.Should().ContainSingle();
        var dto = dtos![0];
        dto.Name.Should().Be("CanEditFeatures");
        dto.Operator.Should().Be(PolicyOperator.All);
        dto.Requirements.Should().ContainSingle(r => r.Type == RequirementType.Permission && r.Value == "features:edit");
    }

    // ── XD01-56: Users/Roles/Permissions admin endpoints ────────────────────────

    /// <summary>Grants exactly the permission codes it's constructed with — drives the
    /// authorized/forbidden split for each endpoint test below. <see cref="CurrentUser"/> backs
    /// <see cref="GetAuthorizationContextAsync"/> for endpoints (e.g. invitations create) that
    /// need both a permission check and the caller's own identity in the same request.</summary>
    private sealed class FakePermissionAuthorizationService(params string[] grantedCodes) : IGeoAuthorizationService
    {
        public AppUser CurrentUser { get; init; } = NewUser("caller@example.com");

        public Task<bool> IsInRoleAsync(string roleName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> HasClaimAsync(string claimType, string? claimValue = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> HasPermissionAsync(string permissionCode, CancellationToken ct = default) => Task.FromResult(grantedCodes.Contains(permissionCode));
        public Task<bool> EvaluatePolicyAsync(string policyName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> EvaluatePolicyAsync(AppPolicy policy, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AuthorizationContext> GetAuthorizationContextAsync(CancellationToken ct = default) =>
            Task.FromResult(new AuthorizationContext { User = CurrentUser, Roles = [], Claims = [], Permissions = grantedCodes });
    }

    private sealed class FakeAdminUserRepository(IReadOnlyDictionary<Guid, AppUser> users, IReadOnlyDictionary<Guid, List<AppRole>> roles) : IUserRepository
    {
        public AppUser? Updated { get; private set; }
        public bool SaveChangesCalled { get; private set; }

        public Task<AppUser?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(users.GetValueOrDefault(id));
        public Task<IReadOnlyList<AppUser>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AppUser>>(users.Values.ToList());
        public Task<IReadOnlyList<AppRole>> GetRolesAsync(Guid userId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AppRole>>(roles.GetValueOrDefault(userId) ?? []);
        public Task UpdateAsync(AppUser user, CancellationToken ct = default)
        {
            Updated = user;
            return Task.CompletedTask;
        }
        public Task SaveChangesAsync(CancellationToken ct = default)
        {
            SaveChangesCalled = true;
            return Task.CompletedTask;
        }

        public Task<AppUser?> GetByExternalObjectIdAsync(string oid, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AppUser?> GetByEmailAsync(string email, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<AppUser>> GetByRoleAsync(string roleName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<AppUser>> GetByOrganizationAsync(Guid organizationId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<AppPermission>> GetEffectivePermissionsAsync(Guid userId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task AddAsync(AppUser user, CancellationToken ct = default) => throw new NotSupportedException();
        public Task AssignRoleAsync(Guid userId, Guid roleId, string? assignedBy = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task RemoveRoleAsync(Guid userId, Guid roleId, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakeAdminRoleRepository(
        IReadOnlyDictionary<Guid, AppRole> roles, IReadOnlyDictionary<Guid, List<AppPermission>> rolePermissions) : IRoleRepository
    {
        public AppRole? Added { get; private set; }
        public AppRole? Updated { get; private set; }
        public Guid? Deleted { get; private set; }
        public (Guid RoleId, Guid PermissionId)? Granted { get; private set; }
        public (Guid RoleId, Guid PermissionId)? Revoked { get; private set; }
        public bool SaveChangesCalled { get; private set; }

        public Task<AppRole?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(roles.GetValueOrDefault(id));
        public Task<IReadOnlyList<AppRole>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AppRole>>(roles.Values.ToList());
        public Task<IReadOnlyList<AppPermission>> GetPermissionsAsync(Guid roleId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AppPermission>>(rolePermissions.GetValueOrDefault(roleId) ?? []);
        public Task AddAsync(AppRole role, CancellationToken ct = default)
        {
            Added = role;
            return Task.CompletedTask;
        }
        public Task UpdateAsync(AppRole role, CancellationToken ct = default)
        {
            Updated = role;
            return Task.CompletedTask;
        }
        public Task DeleteAsync(Guid id, CancellationToken ct = default)
        {
            Deleted = id;
            return Task.CompletedTask;
        }
        public Task GrantPermissionAsync(Guid roleId, Guid permissionId, CancellationToken ct = default)
        {
            Granted = (roleId, permissionId);
            return Task.CompletedTask;
        }
        public Task RevokePermissionAsync(Guid roleId, Guid permissionId, CancellationToken ct = default)
        {
            Revoked = (roleId, permissionId);
            return Task.CompletedTask;
        }
        public Task SaveChangesAsync(CancellationToken ct = default)
        {
            SaveChangesCalled = true;
            return Task.CompletedTask;
        }

        public Task<AppRole?> GetByNameAsync(string name, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakeAdminPermissionRepository(IReadOnlyList<AppPermission> permissions) : IPermissionRepository
    {
        public Task<IReadOnlyList<AppPermission>> GetAllAsync(CancellationToken ct = default) => Task.FromResult(permissions);

        public Task<AppPermission?> GetByIdAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AppPermission?> GetByCodeAsync(string code, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<AppPermission>> GetByResourceAsync(string resource, CancellationToken ct = default) => throw new NotSupportedException();
        public Task AddAsync(AppPermission permission, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpdateAsync(AppPermission permission, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveChangesAsync(CancellationToken ct = default) => throw new NotSupportedException();
    }

    private static AppRole NewRole(string name = "Custom", bool isBuiltIn = false) =>
        new() { Id = Guid.NewGuid(), Name = name, Description = "desc", IsBuiltIn = isBuiltIn };

    private static AppPermission NewPermission(string code) =>
        new() { Id = Guid.NewGuid(), Code = code, Resource = code.Split(':')[0], Action = code.Split(':')[1], Description = code };

    /// <summary>Records what was called and returns a caller-configurable role list — drives
    /// the XD01-63 rolesync endpoint tests below.</summary>
    private sealed class FakeRoleAssignmentProvider : IRoleAssignmentProvider
    {
        public AppRole? RegisteredRole { get; private set; }
        public (string ExternalObjectId, string RoleName)? Assigned { get; private set; }
        public (string ExternalObjectId, string RoleName)? Revoked { get; private set; }
        public IReadOnlyList<string> AssignedRoleNamesToReturn { get; init; } = [];

        public Task RegisterRoleAsync(AppRole role, CancellationToken ct = default)
        {
            RegisteredRole = role;
            return Task.CompletedTask;
        }

        public Task UnregisterRoleAsync(Guid roleId, CancellationToken ct = default) => throw new NotSupportedException();

        public Task AssignRoleAsync(string externalUserObjectId, string roleName, CancellationToken ct = default)
        {
            Assigned = (externalUserObjectId, roleName);
            return Task.CompletedTask;
        }

        public Task RevokeRoleAsync(string externalUserObjectId, string roleName, CancellationToken ct = default)
        {
            Revoked = (externalUserObjectId, roleName);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<string>> GetAssignedRoleNamesAsync(string externalUserObjectId, CancellationToken ct = default)
            => Task.FromResult(AssignedRoleNamesToReturn);
    }

    /// <summary>Never actually invoked — registered purely so invitations endpoints' delegate
    /// metadata can be built for tests that never touch them (see BuildAdminServerAsync).</summary>
    private sealed class NeverCalledPendingInvitationRepository : IPendingInvitationRepository
    {
        public Task<PendingInvitation?> GetByIdAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PendingInvitation?> GetByExternalObjectIdAsync(string externalObjectId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<PendingInvitation>> GetAllPendingAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task AddAsync(PendingInvitation invitation, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpdateAsync(PendingInvitation invitation, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveChangesAsync(CancellationToken ct = default) => throw new NotSupportedException();
    }

    /// <summary>Drives the XD01-69 invitations endpoint tests below.</summary>
    private sealed class FakePendingInvitationRepository(IReadOnlyDictionary<Guid, PendingInvitation> invitations) : IPendingInvitationRepository
    {
        public PendingInvitation? Added { get; private set; }
        public PendingInvitation? Updated { get; private set; }
        public bool SaveChangesCalled { get; private set; }

        public Task<PendingInvitation?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(invitations.GetValueOrDefault(id));
        public Task<IReadOnlyList<PendingInvitation>> GetAllPendingAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<PendingInvitation>>(
                invitations.Values.Where(i => i.Status == InvitationStatus.Pending).ToList());
        public Task AddAsync(PendingInvitation invitation, CancellationToken ct = default)
        {
            Added = invitation;
            return Task.CompletedTask;
        }
        public Task UpdateAsync(PendingInvitation invitation, CancellationToken ct = default)
        {
            Updated = invitation;
            return Task.CompletedTask;
        }
        public Task SaveChangesAsync(CancellationToken ct = default)
        {
            SaveChangesCalled = true;
            return Task.CompletedTask;
        }

        public Task<PendingInvitation?> GetByExternalObjectIdAsync(string externalObjectId, CancellationToken ct = default) => throw new NotSupportedException();
    }

    /// <summary>Drives the XD01-87 userclaims endpoint tests below. Claims are keyed by owning
    /// UserId — the endpoints under test derive "which user" from the caller's auth context, so
    /// looking a claim up under the wrong key is exactly how the non-leakage tests prove it.</summary>
    private sealed class FakeUserClaimRepository(IReadOnlyDictionary<Guid, List<UserClaim>> claimsByUser) : IUserClaimRepository
    {
        public UserClaim? Added { get; private set; }
        public UserClaim? Updated { get; private set; }
        public Guid? Removed { get; private set; }
        public Guid? RemovedAllForUserId { get; private set; }
        public bool SaveChangesCalled { get; private set; }

        public Task<IReadOnlyList<UserClaim>> GetByUserIdAsync(Guid userId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<UserClaim>>(claimsByUser.GetValueOrDefault(userId) ?? []);
        public Task<UserClaim?> GetAsync(Guid userId, string claimType, CancellationToken ct = default) =>
            Task.FromResult((claimsByUser.GetValueOrDefault(userId) ?? []).FirstOrDefault(c => c.Type == claimType));
        public Task AddAsync(UserClaim claim, CancellationToken ct = default)
        {
            Added = claim;
            return Task.CompletedTask;
        }
        public Task UpdateAsync(UserClaim claim, CancellationToken ct = default)
        {
            Updated = claim;
            return Task.CompletedTask;
        }
        public Task RemoveAsync(Guid claimId, CancellationToken ct = default)
        {
            Removed = claimId;
            return Task.CompletedTask;
        }
        public Task RemoveAllAsync(Guid userId, CancellationToken ct = default)
        {
            RemovedAllForUserId = userId;
            return Task.CompletedTask;
        }
        public Task SaveChangesAsync(CancellationToken ct = default)
        {
            SaveChangesCalled = true;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<UserClaim>> GetByTypeAsync(string claimType, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakeUserInvitationProvider : IUserInvitationProvider
    {
        public (string Email, string DisplayName)? CreatedAccount { get; private set; }
        public string ExternalObjectIdToReturn { get; init; } = "new-external-oid";
        public string? RevokedExternalObjectId { get; private set; }

        public Task<string> CreateInvitedAccountAsync(string email, string displayName, CancellationToken ct = default)
        {
            CreatedAccount = (email, displayName);
            return Task.FromResult(ExternalObjectIdToReturn);
        }

        public Task RevokeInvitedAccountAsync(string externalObjectId, CancellationToken ct = default)
        {
            RevokedExternalObjectId = externalObjectId;
            return Task.CompletedTask;
        }
    }

    /// <summary>Set <see cref="ThrowOnSend"/> to simulate an ACS send failure without touching
    /// the account/invitation-row that were already created before the send is attempted.</summary>
    private sealed class FakeInvitationEmailSender : IInvitationEmailSender
    {
        public (string ToEmail, string DisplayName)? Sent { get; private set; }
        public bool ThrowOnSend { get; init; }

        public Task SendInvitationAsync(string toEmail, string displayName, CancellationToken ct = default)
        {
            if (ThrowOnSend) throw new InvalidOperationException("Simulated ACS send failure.");
            Sent = (toEmail, displayName);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Real ASP.NET Core auth pipeline (AddGeoAuthorizationPolicyBridge + a no-op scheme),
    /// mirroring GeoAuthorizationPolicyBridgeEndToEndTests — needed because, unlike /me and
    /// /policies, the admin endpoints call .RequireAuthorization("resource:action").
    /// </summary>
    private static async Task<TestServer> BuildAdminServerAsync(
        IUserRepository userRepo, IRoleRepository roleRepo, IPermissionRepository permissionRepo,
        IGeoAuthorizationService authService, IRoleAssignmentProvider? roleSync = null,
        IPendingInvitationRepository? invitationRepo = null,
        IUserInvitationProvider? invitationProvider = null,
        IInvitationEmailSender? invitationEmailSender = null,
        IUserClaimRepository? claimRepo = null)
    {
        var host = await new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddAuthentication("Test")
                        .AddScheme<AuthenticationSchemeOptions, NoOpAuthenticationHandler>("Test", _ => { });
                    services.AddAuthorization();
                    services.AddGeoAuthorizationPolicyBridge();
                    services.AddSingleton(authService);
                    services.AddSingleton(userRepo);
                    services.AddSingleton(roleRepo);
                    services.AddSingleton(permissionRepo);
                    services.AddSingleton<IOrganizationGrantRepository, NeverCalledOrganizationGrantRepository>();
                    // MapIdentityApi() maps /me and /policies too; endpoint metadata for the
                    // whole group is built lazily on first request, so IPolicyRepository must
                    // be resolvable even though these tests never call /policies.
                    services.AddSingleton<IPolicyRepository>(new FakePolicyRepository([]));
                    services.AddSingleton<IRoleAssignmentProvider>(roleSync ?? new NullRoleAssignmentProvider());
                    // Same reasoning — invitations endpoints are mapped by the same MapIdentityApi()
                    // call, so these three must be resolvable even for tests that never touch them.
                    services.AddSingleton<IPendingInvitationRepository>(invitationRepo ?? new NeverCalledPendingInvitationRepository());
                    services.AddSingleton<IUserInvitationProvider>(invitationProvider ?? new NullUserInvitationProvider());
                    services.AddSingleton<IInvitationEmailSender>(invitationEmailSender ?? new NullInvitationEmailSender());
                    services.AddSingleton<IUserClaimRepository>(claimRepo ?? new FakeUserClaimRepository(new Dictionary<Guid, List<UserClaim>>()));
                });
                webHost.Configure(app =>
                {
                    app.Use(async (ctx, next) =>
                    {
                        if (ctx.Request.Headers.ContainsKey("X-Test-Authenticated"))
                            ctx.User = new ClaimsPrincipal(new ClaimsIdentity(
                                [new Claim(ClaimTypes.NameIdentifier, "test-user")], "TestScheme"));
                        await next();
                    });
                    app.UseRouting();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints => endpoints.MapIdentityApi());
                });
            })
            .StartAsync();

        return host.GetTestServer();
    }

    private static HttpClient AuthenticatedClient(TestServer server)
    {
        var client = server.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Authenticated", "1");
        return client;
    }

    // ── Users ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Users_List_Authorized_ReturnsSummaries()
    {
        var user = NewUser();
        var userRepo = new FakeAdminUserRepository(
            new Dictionary<Guid, AppUser> { [user.Id] = user }, new Dictionary<Guid, List<AppRole>>());
        using var server = await BuildAdminServerAsync(
            userRepo, new FakeAdminRoleRepository(new Dictionary<Guid, AppRole>(), new Dictionary<Guid, List<AppPermission>>()),
            new FakeAdminPermissionRepository([]), new FakePermissionAuthorizationService("users:read"));
        using var client = AuthenticatedClient(server);

        var response = await client.GetAsync("/api/identity/users");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dtos = await response.Content.ReadFromJsonAsync<List<UserSummaryDto>>();
        dtos.Should().ContainSingle(d => d.Id == user.Id && d.Email == user.Email);
    }

    [Fact]
    public async Task Users_List_Forbidden_Returns403()
    {
        using var server = await BuildAdminServerAsync(
            new FakeAdminUserRepository(new Dictionary<Guid, AppUser>(), new Dictionary<Guid, List<AppRole>>()),
            new FakeAdminRoleRepository(new Dictionary<Guid, AppRole>(), new Dictionary<Guid, List<AppPermission>>()),
            new FakeAdminPermissionRepository([]), new FakePermissionAuthorizationService());
        using var client = AuthenticatedClient(server);

        var response = await client.GetAsync("/api/identity/users");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Users_GetById_Authorized_ReturnsDetailWithRoleIds()
    {
        var user = NewUser();
        var role = NewRole("Supervisor", isBuiltIn: true);
        var userRepo = new FakeAdminUserRepository(
            new Dictionary<Guid, AppUser> { [user.Id] = user },
            new Dictionary<Guid, List<AppRole>> { [user.Id] = [role] });
        using var server = await BuildAdminServerAsync(
            userRepo, new FakeAdminRoleRepository(new Dictionary<Guid, AppRole>(), new Dictionary<Guid, List<AppPermission>>()),
            new FakeAdminPermissionRepository([]), new FakePermissionAuthorizationService("users:read"));
        using var client = AuthenticatedClient(server);

        var dto = await client.GetFromJsonAsync<UserDetailDto>($"/api/identity/users/{user.Id}");

        dto.Should().NotBeNull();
        dto!.Id.Should().Be(user.Id);
        dto.RoleIds.Should().ContainSingle().Which.Should().Be(role.Id);
    }

    [Fact]
    public async Task Users_GetById_NotFound_Returns404()
    {
        using var server = await BuildAdminServerAsync(
            new FakeAdminUserRepository(new Dictionary<Guid, AppUser>(), new Dictionary<Guid, List<AppRole>>()),
            new FakeAdminRoleRepository(new Dictionary<Guid, AppRole>(), new Dictionary<Guid, List<AppPermission>>()),
            new FakeAdminPermissionRepository([]), new FakePermissionAuthorizationService("users:read"));
        using var client = AuthenticatedClient(server);

        var response = await client.GetAsync($"/api/identity/users/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Users_GetById_Forbidden_Returns403()
    {
        using var server = await BuildAdminServerAsync(
            new FakeAdminUserRepository(new Dictionary<Guid, AppUser>(), new Dictionary<Guid, List<AppRole>>()),
            new FakeAdminRoleRepository(new Dictionary<Guid, AppRole>(), new Dictionary<Guid, List<AppPermission>>()),
            new FakeAdminPermissionRepository([]), new FakePermissionAuthorizationService());
        using var client = AuthenticatedClient(server);

        var response = await client.GetAsync($"/api/identity/users/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Users_Update_Authorized_UpdatesAndReturns204()
    {
        var user = NewUser();
        var userRepo = new FakeAdminUserRepository(
            new Dictionary<Guid, AppUser> { [user.Id] = user }, new Dictionary<Guid, List<AppRole>>());
        using var server = await BuildAdminServerAsync(
            userRepo, new FakeAdminRoleRepository(new Dictionary<Guid, AppRole>(), new Dictionary<Guid, List<AppPermission>>()),
            new FakeAdminPermissionRepository([]), new FakePermissionAuthorizationService("users:edit"));
        using var client = AuthenticatedClient(server);
        var orgId = Guid.NewGuid();

        var response = await client.PutAsJsonAsync($"/api/identity/users/{user.Id}",
            new UserUpdateDto("New Name", false, orgId));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        userRepo.Updated.Should().NotBeNull();
        userRepo.Updated!.DisplayName.Should().Be("New Name");
        userRepo.Updated.IsActive.Should().BeFalse();
        userRepo.Updated.OrganizationId.Should().Be(orgId);
        userRepo.SaveChangesCalled.Should().BeTrue();
    }

    [Fact]
    public async Task Users_Update_NotFound_Returns404()
    {
        using var server = await BuildAdminServerAsync(
            new FakeAdminUserRepository(new Dictionary<Guid, AppUser>(), new Dictionary<Guid, List<AppRole>>()),
            new FakeAdminRoleRepository(new Dictionary<Guid, AppRole>(), new Dictionary<Guid, List<AppPermission>>()),
            new FakeAdminPermissionRepository([]), new FakePermissionAuthorizationService("users:edit"));
        using var client = AuthenticatedClient(server);

        var response = await client.PutAsJsonAsync($"/api/identity/users/{Guid.NewGuid()}",
            new UserUpdateDto("Name", true, null));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Users_Update_Forbidden_Returns403()
    {
        var user = NewUser();
        using var server = await BuildAdminServerAsync(
            new FakeAdminUserRepository(new Dictionary<Guid, AppUser> { [user.Id] = user }, new Dictionary<Guid, List<AppRole>>()),
            new FakeAdminRoleRepository(new Dictionary<Guid, AppRole>(), new Dictionary<Guid, List<AppPermission>>()),
            new FakeAdminPermissionRepository([]), new FakePermissionAuthorizationService());
        using var client = AuthenticatedClient(server);

        var response = await client.PutAsJsonAsync($"/api/identity/users/{user.Id}",
            new UserUpdateDto("Name", true, null));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── Roles ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Roles_List_Authorized_ReturnsSummaries()
    {
        var role = NewRole();
        using var server = await BuildAdminServerAsync(
            new FakeAdminUserRepository(new Dictionary<Guid, AppUser>(), new Dictionary<Guid, List<AppRole>>()),
            new FakeAdminRoleRepository(new Dictionary<Guid, AppRole> { [role.Id] = role }, new Dictionary<Guid, List<AppPermission>>()),
            new FakeAdminPermissionRepository([]), new FakePermissionAuthorizationService("roles:read"));
        using var client = AuthenticatedClient(server);

        var dtos = await client.GetFromJsonAsync<List<RoleSummaryDto>>("/api/identity/roles");

        dtos.Should().ContainSingle(d => d.Id == role.Id && d.Name == role.Name);
    }

    [Fact]
    public async Task Roles_List_Forbidden_Returns403()
    {
        using var server = await BuildAdminServerAsync(
            new FakeAdminUserRepository(new Dictionary<Guid, AppUser>(), new Dictionary<Guid, List<AppRole>>()),
            new FakeAdminRoleRepository(new Dictionary<Guid, AppRole>(), new Dictionary<Guid, List<AppPermission>>()),
            new FakeAdminPermissionRepository([]), new FakePermissionAuthorizationService());
        using var client = AuthenticatedClient(server);

        var response = await client.GetAsync("/api/identity/roles");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Roles_GetById_Authorized_ReturnsDetailWithPermissionIds()
    {
        var role = NewRole();
        var permission = NewPermission("features:read");
        using var server = await BuildAdminServerAsync(
            new FakeAdminUserRepository(new Dictionary<Guid, AppUser>(), new Dictionary<Guid, List<AppRole>>()),
            new FakeAdminRoleRepository(
                new Dictionary<Guid, AppRole> { [role.Id] = role },
                new Dictionary<Guid, List<AppPermission>> { [role.Id] = [permission] }),
            new FakeAdminPermissionRepository([]), new FakePermissionAuthorizationService("roles:read"));
        using var client = AuthenticatedClient(server);

        var dto = await client.GetFromJsonAsync<RoleDetailDto>($"/api/identity/roles/{role.Id}");

        dto.Should().NotBeNull();
        dto!.PermissionIds.Should().ContainSingle().Which.Should().Be(permission.Id);
    }

    [Fact]
    public async Task Roles_GetById_NotFound_Returns404()
    {
        using var server = await BuildAdminServerAsync(
            new FakeAdminUserRepository(new Dictionary<Guid, AppUser>(), new Dictionary<Guid, List<AppRole>>()),
            new FakeAdminRoleRepository(new Dictionary<Guid, AppRole>(), new Dictionary<Guid, List<AppPermission>>()),
            new FakeAdminPermissionRepository([]), new FakePermissionAuthorizationService("roles:read"));
        using var client = AuthenticatedClient(server);

        var response = await client.GetAsync($"/api/identity/roles/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Roles_GetById_Forbidden_Returns403()
    {
        using var server = await BuildAdminServerAsync(
            new FakeAdminUserRepository(new Dictionary<Guid, AppUser>(), new Dictionary<Guid, List<AppRole>>()),
            new FakeAdminRoleRepository(new Dictionary<Guid, AppRole>(), new Dictionary<Guid, List<AppPermission>>()),
            new FakeAdminPermissionRepository([]), new FakePermissionAuthorizationService());
        using var client = AuthenticatedClient(server);

        var response = await client.GetAsync($"/api/identity/roles/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Roles_Create_Authorized_ReturnsCreatedWithIsBuiltInFalse()
    {
        var roleRepo = new FakeAdminRoleRepository(new Dictionary<Guid, AppRole>(), new Dictionary<Guid, List<AppPermission>>());
        using var server = await BuildAdminServerAsync(
            new FakeAdminUserRepository(new Dictionary<Guid, AppUser>(), new Dictionary<Guid, List<AppRole>>()),
            roleRepo, new FakeAdminPermissionRepository([]), new FakePermissionAuthorizationService("roles:edit"));
        using var client = AuthenticatedClient(server);

        var response = await client.PostAsJsonAsync("/api/identity/roles", new RoleWriteDto("Auditor", "desc"));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        roleRepo.Added.Should().NotBeNull();
        roleRepo.Added!.Name.Should().Be("Auditor");
        roleRepo.Added.IsBuiltIn.Should().BeFalse();
        roleRepo.SaveChangesCalled.Should().BeTrue();
    }

    [Fact]
    public async Task Roles_Create_Forbidden_Returns403()
    {
        using var server = await BuildAdminServerAsync(
            new FakeAdminUserRepository(new Dictionary<Guid, AppUser>(), new Dictionary<Guid, List<AppRole>>()),
            new FakeAdminRoleRepository(new Dictionary<Guid, AppRole>(), new Dictionary<Guid, List<AppPermission>>()),
            new FakeAdminPermissionRepository([]), new FakePermissionAuthorizationService());
        using var client = AuthenticatedClient(server);

        var response = await client.PostAsJsonAsync("/api/identity/roles", new RoleWriteDto("Auditor", "desc"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Roles_Update_Authorized_UpdatesNameAndDescription()
    {
        var role = NewRole();
        var roleRepo = new FakeAdminRoleRepository(
            new Dictionary<Guid, AppRole> { [role.Id] = role }, new Dictionary<Guid, List<AppPermission>>());
        using var server = await BuildAdminServerAsync(
            new FakeAdminUserRepository(new Dictionary<Guid, AppUser>(), new Dictionary<Guid, List<AppRole>>()),
            roleRepo, new FakeAdminPermissionRepository([]), new FakePermissionAuthorizationService("roles:edit"));
        using var client = AuthenticatedClient(server);

        var response = await client.PutAsJsonAsync($"/api/identity/roles/{role.Id}", new RoleWriteDto("Renamed", "new desc"));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        roleRepo.Updated.Should().NotBeNull();
        roleRepo.Updated!.Name.Should().Be("Renamed");
        roleRepo.Updated.Description.Should().Be("new desc");
    }

    [Fact]
    public async Task Roles_Update_NotFound_Returns404()
    {
        using var server = await BuildAdminServerAsync(
            new FakeAdminUserRepository(new Dictionary<Guid, AppUser>(), new Dictionary<Guid, List<AppRole>>()),
            new FakeAdminRoleRepository(new Dictionary<Guid, AppRole>(), new Dictionary<Guid, List<AppPermission>>()),
            new FakeAdminPermissionRepository([]), new FakePermissionAuthorizationService("roles:edit"));
        using var client = AuthenticatedClient(server);

        var response = await client.PutAsJsonAsync($"/api/identity/roles/{Guid.NewGuid()}", new RoleWriteDto("X", "Y"));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Roles_Update_Forbidden_Returns403()
    {
        var role = NewRole();
        using var server = await BuildAdminServerAsync(
            new FakeAdminUserRepository(new Dictionary<Guid, AppUser>(), new Dictionary<Guid, List<AppRole>>()),
            new FakeAdminRoleRepository(new Dictionary<Guid, AppRole> { [role.Id] = role }, new Dictionary<Guid, List<AppPermission>>()),
            new FakeAdminPermissionRepository([]), new FakePermissionAuthorizationService());
        using var client = AuthenticatedClient(server);

        var response = await client.PutAsJsonAsync($"/api/identity/roles/{role.Id}", new RoleWriteDto("X", "Y"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Roles_Delete_BuiltIn_Returns409()
    {
        var role = NewRole("Administrator", isBuiltIn: true);
        var roleRepo = new FakeAdminRoleRepository(
            new Dictionary<Guid, AppRole> { [role.Id] = role }, new Dictionary<Guid, List<AppPermission>>());
        using var server = await BuildAdminServerAsync(
            new FakeAdminUserRepository(new Dictionary<Guid, AppUser>(), new Dictionary<Guid, List<AppRole>>()),
            roleRepo, new FakeAdminPermissionRepository([]), new FakePermissionAuthorizationService("roles:delete"));
        using var client = AuthenticatedClient(server);

        var response = await client.DeleteAsync($"/api/identity/roles/{role.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        roleRepo.Deleted.Should().BeNull();
    }

    [Fact]
    public async Task Roles_Delete_Custom_Returns204()
    {
        var role = NewRole("Auditor", isBuiltIn: false);
        var roleRepo = new FakeAdminRoleRepository(
            new Dictionary<Guid, AppRole> { [role.Id] = role }, new Dictionary<Guid, List<AppPermission>>());
        using var server = await BuildAdminServerAsync(
            new FakeAdminUserRepository(new Dictionary<Guid, AppUser>(), new Dictionary<Guid, List<AppRole>>()),
            roleRepo, new FakeAdminPermissionRepository([]), new FakePermissionAuthorizationService("roles:delete"));
        using var client = AuthenticatedClient(server);

        var response = await client.DeleteAsync($"/api/identity/roles/{role.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        roleRepo.Deleted.Should().Be(role.Id);
        roleRepo.SaveChangesCalled.Should().BeTrue();
    }

    [Fact]
    public async Task Roles_Delete_NotFound_Returns404()
    {
        using var server = await BuildAdminServerAsync(
            new FakeAdminUserRepository(new Dictionary<Guid, AppUser>(), new Dictionary<Guid, List<AppRole>>()),
            new FakeAdminRoleRepository(new Dictionary<Guid, AppRole>(), new Dictionary<Guid, List<AppPermission>>()),
            new FakeAdminPermissionRepository([]), new FakePermissionAuthorizationService("roles:delete"));
        using var client = AuthenticatedClient(server);

        var response = await client.DeleteAsync($"/api/identity/roles/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Roles_Delete_Forbidden_Returns403()
    {
        var role = NewRole();
        using var server = await BuildAdminServerAsync(
            new FakeAdminUserRepository(new Dictionary<Guid, AppUser>(), new Dictionary<Guid, List<AppRole>>()),
            new FakeAdminRoleRepository(new Dictionary<Guid, AppRole> { [role.Id] = role }, new Dictionary<Guid, List<AppPermission>>()),
            new FakeAdminPermissionRepository([]), new FakePermissionAuthorizationService());
        using var client = AuthenticatedClient(server);

        var response = await client.DeleteAsync($"/api/identity/roles/{role.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Roles_GrantPermission_Authorized_Returns204()
    {
        var roleId = Guid.NewGuid();
        var permId = Guid.NewGuid();
        var roleRepo = new FakeAdminRoleRepository(new Dictionary<Guid, AppRole>(), new Dictionary<Guid, List<AppPermission>>());
        using var server = await BuildAdminServerAsync(
            new FakeAdminUserRepository(new Dictionary<Guid, AppUser>(), new Dictionary<Guid, List<AppRole>>()),
            roleRepo, new FakeAdminPermissionRepository([]), new FakePermissionAuthorizationService("roles:edit"));
        using var client = AuthenticatedClient(server);

        var response = await client.PostAsync($"/api/identity/roles/{roleId}/permissions/{permId}", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        roleRepo.Granted.Should().Be((roleId, permId));
        roleRepo.SaveChangesCalled.Should().BeTrue();
    }

    [Fact]
    public async Task Roles_GrantPermission_Forbidden_Returns403()
    {
        using var server = await BuildAdminServerAsync(
            new FakeAdminUserRepository(new Dictionary<Guid, AppUser>(), new Dictionary<Guid, List<AppRole>>()),
            new FakeAdminRoleRepository(new Dictionary<Guid, AppRole>(), new Dictionary<Guid, List<AppPermission>>()),
            new FakeAdminPermissionRepository([]), new FakePermissionAuthorizationService());
        using var client = AuthenticatedClient(server);

        var response = await client.PostAsync($"/api/identity/roles/{Guid.NewGuid()}/permissions/{Guid.NewGuid()}", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Roles_RevokePermission_Authorized_Returns204()
    {
        var roleId = Guid.NewGuid();
        var permId = Guid.NewGuid();
        var roleRepo = new FakeAdminRoleRepository(new Dictionary<Guid, AppRole>(), new Dictionary<Guid, List<AppPermission>>());
        using var server = await BuildAdminServerAsync(
            new FakeAdminUserRepository(new Dictionary<Guid, AppUser>(), new Dictionary<Guid, List<AppRole>>()),
            roleRepo, new FakeAdminPermissionRepository([]), new FakePermissionAuthorizationService("roles:edit"));
        using var client = AuthenticatedClient(server);

        var response = await client.DeleteAsync($"/api/identity/roles/{roleId}/permissions/{permId}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        roleRepo.Revoked.Should().Be((roleId, permId));
        roleRepo.SaveChangesCalled.Should().BeTrue();
    }

    [Fact]
    public async Task Roles_RevokePermission_Forbidden_Returns403()
    {
        using var server = await BuildAdminServerAsync(
            new FakeAdminUserRepository(new Dictionary<Guid, AppUser>(), new Dictionary<Guid, List<AppRole>>()),
            new FakeAdminRoleRepository(new Dictionary<Guid, AppRole>(), new Dictionary<Guid, List<AppPermission>>()),
            new FakeAdminPermissionRepository([]), new FakePermissionAuthorizationService());
        using var client = AuthenticatedClient(server);

        var response = await client.DeleteAsync($"/api/identity/roles/{Guid.NewGuid()}/permissions/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── Permissions ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Permissions_List_Authorized_ReturnsAll()
    {
        var permission = NewPermission("reports:export");
        using var server = await BuildAdminServerAsync(
            new FakeAdminUserRepository(new Dictionary<Guid, AppUser>(), new Dictionary<Guid, List<AppRole>>()),
            new FakeAdminRoleRepository(new Dictionary<Guid, AppRole>(), new Dictionary<Guid, List<AppPermission>>()),
            new FakeAdminPermissionRepository([permission]), new FakePermissionAuthorizationService("permissions:read"));
        using var client = AuthenticatedClient(server);

        var dtos = await client.GetFromJsonAsync<List<PermissionDto>>("/api/identity/permissions");

        dtos.Should().ContainSingle(d => d.Id == permission.Id && d.Code == "reports:export");
    }

    [Fact]
    public async Task Permissions_List_Forbidden_Returns403()
    {
        using var server = await BuildAdminServerAsync(
            new FakeAdminUserRepository(new Dictionary<Guid, AppUser>(), new Dictionary<Guid, List<AppRole>>()),
            new FakeAdminRoleRepository(new Dictionary<Guid, AppRole>(), new Dictionary<Guid, List<AppPermission>>()),
            new FakeAdminPermissionRepository([]), new FakePermissionAuthorizationService());
        using var client = AuthenticatedClient(server);

        var response = await client.GetAsync("/api/identity/permissions");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── Role Sync (XD01-59 Phase 2, XD01-63) ────────────────────────────────

    private static (IUserRepository, IRoleRepository, IPermissionRepository) EmptyAdminRepos() => (
        new FakeAdminUserRepository(new Dictionary<Guid, AppUser>(), new Dictionary<Guid, List<AppRole>>()),
        new FakeAdminRoleRepository(new Dictionary<Guid, AppRole>(), new Dictionary<Guid, List<AppPermission>>()),
        new FakeAdminPermissionRepository([]));

    [Fact]
    public async Task RoleSync_Status_NullProvider_ReturnsDisabled()
    {
        var (userRepo, roleRepo, permRepo) = EmptyAdminRepos();
        using var server = await BuildAdminServerAsync(
            userRepo, roleRepo, permRepo, new FakePermissionAuthorizationService(), new NullRoleAssignmentProvider());
        using var client = server.CreateClient();

        var dto = await client.GetFromJsonAsync<RoleSyncStatusDto>("/api/identity/rolesync/status");

        dto.Should().NotBeNull();
        dto!.Enabled.Should().BeFalse();
    }

    [Fact]
    public async Task RoleSync_Status_RealProvider_ReturnsEnabled()
    {
        var (userRepo, roleRepo, permRepo) = EmptyAdminRepos();
        using var server = await BuildAdminServerAsync(
            userRepo, roleRepo, permRepo, new FakePermissionAuthorizationService(), new FakeRoleAssignmentProvider());
        using var client = server.CreateClient();

        var dto = await client.GetFromJsonAsync<RoleSyncStatusDto>("/api/identity/rolesync/status");

        dto.Should().NotBeNull();
        dto!.Enabled.Should().BeTrue();
    }

    [Fact]
    public async Task RoleSync_RegisterRole_Authorized_CallsProviderAndReturns204()
    {
        var role = NewRole("Supervisor");
        var roleRepo = new FakeAdminRoleRepository(
            new Dictionary<Guid, AppRole> { [role.Id] = role }, new Dictionary<Guid, List<AppPermission>>());
        var roleSync = new FakeRoleAssignmentProvider();
        using var server = await BuildAdminServerAsync(
            new FakeAdminUserRepository(new Dictionary<Guid, AppUser>(), new Dictionary<Guid, List<AppRole>>()),
            roleRepo, new FakeAdminPermissionRepository([]), new FakePermissionAuthorizationService("roles:edit"), roleSync);
        using var client = AuthenticatedClient(server);

        var response = await client.PostAsync($"/api/identity/rolesync/roles/{role.Id}", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        roleSync.RegisteredRole.Should().Be(role);
    }

    [Fact]
    public async Task RoleSync_RegisterRole_NotFound_Returns404()
    {
        var roleSync = new FakeRoleAssignmentProvider();
        var (userRepo, roleRepo, permRepo) = EmptyAdminRepos();
        using var server = await BuildAdminServerAsync(
            userRepo, roleRepo, permRepo, new FakePermissionAuthorizationService("roles:edit"), roleSync);
        using var client = AuthenticatedClient(server);

        var response = await client.PostAsync($"/api/identity/rolesync/roles/{Guid.NewGuid()}", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        roleSync.RegisteredRole.Should().BeNull();
    }

    [Fact]
    public async Task RoleSync_RegisterRole_Forbidden_Returns403()
    {
        var (userRepo, roleRepo, permRepo) = EmptyAdminRepos();
        using var server = await BuildAdminServerAsync(
            userRepo, roleRepo, permRepo, new FakePermissionAuthorizationService());
        using var client = AuthenticatedClient(server);

        var response = await client.PostAsync($"/api/identity/rolesync/roles/{Guid.NewGuid()}", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task RoleSync_AssignRole_Authorized_CallsProviderAndReturns204()
    {
        var roleSync = new FakeRoleAssignmentProvider();
        var (userRepo, roleRepo, permRepo) = EmptyAdminRepos();
        using var server = await BuildAdminServerAsync(
            userRepo, roleRepo, permRepo, new FakePermissionAuthorizationService("users:edit"), roleSync);
        using var client = AuthenticatedClient(server);

        var response = await client.PostAsync("/api/identity/rolesync/users/ext-oid-1/roles/Supervisor", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        roleSync.Assigned.Should().Be(("ext-oid-1", "Supervisor"));
    }

    [Fact]
    public async Task RoleSync_AssignRole_Forbidden_Returns403()
    {
        var (userRepo, roleRepo, permRepo) = EmptyAdminRepos();
        using var server = await BuildAdminServerAsync(
            userRepo, roleRepo, permRepo, new FakePermissionAuthorizationService());
        using var client = AuthenticatedClient(server);

        var response = await client.PostAsync("/api/identity/rolesync/users/ext-oid-1/roles/Supervisor", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task RoleSync_RevokeRole_Authorized_CallsProviderAndReturns204()
    {
        var roleSync = new FakeRoleAssignmentProvider();
        var (userRepo, roleRepo, permRepo) = EmptyAdminRepos();
        using var server = await BuildAdminServerAsync(
            userRepo, roleRepo, permRepo, new FakePermissionAuthorizationService("users:edit"), roleSync);
        using var client = AuthenticatedClient(server);

        var response = await client.DeleteAsync("/api/identity/rolesync/users/ext-oid-1/roles/Supervisor");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        roleSync.Revoked.Should().Be(("ext-oid-1", "Supervisor"));
    }

    [Fact]
    public async Task RoleSync_RevokeRole_Forbidden_Returns403()
    {
        var (userRepo, roleRepo, permRepo) = EmptyAdminRepos();
        using var server = await BuildAdminServerAsync(
            userRepo, roleRepo, permRepo, new FakePermissionAuthorizationService());
        using var client = AuthenticatedClient(server);

        var response = await client.DeleteAsync("/api/identity/rolesync/users/ext-oid-1/roles/Supervisor");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task RoleSync_GetAssignedRoles_Authorized_ReturnsNames()
    {
        var roleSync = new FakeRoleAssignmentProvider { AssignedRoleNamesToReturn = ["Supervisor", "Auditor"] };
        var (userRepo, roleRepo, permRepo) = EmptyAdminRepos();
        using var server = await BuildAdminServerAsync(
            userRepo, roleRepo, permRepo, new FakePermissionAuthorizationService("users:read"), roleSync);
        using var client = AuthenticatedClient(server);

        var names = await client.GetFromJsonAsync<List<string>>("/api/identity/rolesync/users/ext-oid-1/roles");

        names.Should().BeEquivalentTo(["Supervisor", "Auditor"]);
    }

    [Fact]
    public async Task RoleSync_GetAssignedRoles_Forbidden_Returns403()
    {
        var (userRepo, roleRepo, permRepo) = EmptyAdminRepos();
        using var server = await BuildAdminServerAsync(
            userRepo, roleRepo, permRepo, new FakePermissionAuthorizationService());
        using var client = AuthenticatedClient(server);

        var response = await client.GetAsync("/api/identity/rolesync/users/ext-oid-1/roles");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── Invitations (XD01-59 Phase 3, XD01-69) ──────────────────────────────

    private static PendingInvitation NewInvitation(
        string email = "invitee@example.com", string externalObjectId = "invitee-oid",
        InvitationStatus status = InvitationStatus.Pending) => new()
    {
        Id               = Guid.NewGuid(),
        Email            = email,
        ExternalObjectId = externalObjectId,
        InvitedByUserId  = Guid.NewGuid(),
        InvitedAt        = DateTime.UtcNow,
        Status           = status,
    };

    [Fact]
    public async Task Invitations_Status_BothProvidersReal_ReturnsEnabled()
    {
        var (userRepo, roleRepo, permRepo) = EmptyAdminRepos();
        using var server = await BuildAdminServerAsync(
            userRepo, roleRepo, permRepo, new FakePermissionAuthorizationService(),
            invitationProvider: new FakeUserInvitationProvider(), invitationEmailSender: new FakeInvitationEmailSender());
        using var client = server.CreateClient();

        var dto = await client.GetFromJsonAsync<InvitationStatusDto>("/api/identity/invitations/status");

        dto.Should().NotBeNull();
        dto!.Enabled.Should().BeTrue();
    }

    [Fact]
    public async Task Invitations_Status_BothProvidersNull_ReturnsDisabled()
    {
        var (userRepo, roleRepo, permRepo) = EmptyAdminRepos();
        using var server = await BuildAdminServerAsync(
            userRepo, roleRepo, permRepo, new FakePermissionAuthorizationService());
        using var client = server.CreateClient();

        var dto = await client.GetFromJsonAsync<InvitationStatusDto>("/api/identity/invitations/status");

        dto.Should().NotBeNull();
        dto!.Enabled.Should().BeFalse();
    }

    [Fact]
    public async Task Invitations_Status_OnlyOneProviderReal_ReturnsDisabled()
    {
        // Proves the status check requires *both* the account provider and the email sender to
        // be real — a half-configured Invitation feature (e.g. XD01-67 wired but XD01-68 isn't)
        // must not report itself as usable.
        var (userRepo, roleRepo, permRepo) = EmptyAdminRepos();
        using var server = await BuildAdminServerAsync(
            userRepo, roleRepo, permRepo, new FakePermissionAuthorizationService(),
            invitationProvider: new FakeUserInvitationProvider());
        using var client = server.CreateClient();

        var dto = await client.GetFromJsonAsync<InvitationStatusDto>("/api/identity/invitations/status");

        dto.Should().NotBeNull();
        dto!.Enabled.Should().BeFalse();
    }

    [Fact]
    public async Task Invitations_List_Authorized_ReturnsOnlyPendingInvitations()
    {
        var pending = NewInvitation(status: InvitationStatus.Pending);
        var redeemed = NewInvitation(status: InvitationStatus.Redeemed);
        var invitationRepo = new FakePendingInvitationRepository(
            new Dictionary<Guid, PendingInvitation> { [pending.Id] = pending, [redeemed.Id] = redeemed });
        var (userRepo, roleRepo, permRepo) = EmptyAdminRepos();
        using var server = await BuildAdminServerAsync(
            userRepo, roleRepo, permRepo, new FakePermissionAuthorizationService("users:read"),
            invitationRepo: invitationRepo);
        using var client = AuthenticatedClient(server);

        var dtos = await client.GetFromJsonAsync<List<PendingInvitationDto>>("/api/identity/invitations");

        dtos.Should().ContainSingle(d => d.Id == pending.Id);
    }

    [Fact]
    public async Task Invitations_List_Forbidden_Returns403()
    {
        var (userRepo, roleRepo, permRepo) = EmptyAdminRepos();
        using var server = await BuildAdminServerAsync(
            userRepo, roleRepo, permRepo, new FakePermissionAuthorizationService());
        using var client = AuthenticatedClient(server);

        var response = await client.GetAsync("/api/identity/invitations");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Invitations_Create_Authorized_CreatesAccountPersistsRowAndSendsEmail_Returns201()
    {
        var caller = NewUser("admin@example.com");
        var invitationProvider = new FakeUserInvitationProvider { ExternalObjectIdToReturn = "new-invitee-oid" };
        var emailSender = new FakeInvitationEmailSender();
        var invitationRepo = new FakePendingInvitationRepository(new Dictionary<Guid, PendingInvitation>());
        var (userRepo, roleRepo, permRepo) = EmptyAdminRepos();
        using var server = await BuildAdminServerAsync(
            userRepo, roleRepo, permRepo, new FakePermissionAuthorizationService("users:edit") { CurrentUser = caller },
            invitationRepo: invitationRepo, invitationProvider: invitationProvider, invitationEmailSender: emailSender);
        using var client = AuthenticatedClient(server);

        var response = await client.PostAsJsonAsync(
            "/api/identity/invitations", new InvitationCreateDto("invitee@example.com", "Invitee Name"));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        invitationProvider.CreatedAccount.Should().Be(("invitee@example.com", "Invitee Name"));
        emailSender.Sent.Should().Be(("invitee@example.com", "Invitee Name"));
        invitationRepo.Added.Should().NotBeNull();
        invitationRepo.Added!.Email.Should().Be("invitee@example.com");
        invitationRepo.Added.ExternalObjectId.Should().Be("new-invitee-oid");
        invitationRepo.Added.InvitedByUserId.Should().Be(caller.Id);
        invitationRepo.Added.Status.Should().Be(InvitationStatus.Pending);
        invitationRepo.SaveChangesCalled.Should().BeTrue();

        var dto = await response.Content.ReadFromJsonAsync<PendingInvitationDto>();
        dto!.ExternalObjectId.Should().Be("new-invitee-oid");
    }

    [Fact]
    public async Task Invitations_Create_EmailSendFails_StillPersistsInvitation_Returns202()
    {
        // The account + PendingInvitation row must survive a failed email send — losing track of
        // an already-created provider account would be worse than a merely-undelivered email.
        var invitationProvider = new FakeUserInvitationProvider();
        var emailSender = new FakeInvitationEmailSender { ThrowOnSend = true };
        var invitationRepo = new FakePendingInvitationRepository(new Dictionary<Guid, PendingInvitation>());
        var (userRepo, roleRepo, permRepo) = EmptyAdminRepos();
        using var server = await BuildAdminServerAsync(
            userRepo, roleRepo, permRepo, new FakePermissionAuthorizationService("users:edit"),
            invitationRepo: invitationRepo, invitationProvider: invitationProvider, invitationEmailSender: emailSender);
        using var client = AuthenticatedClient(server);

        var response = await client.PostAsJsonAsync(
            "/api/identity/invitations", new InvitationCreateDto("invitee@example.com", "Invitee Name"));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        invitationRepo.Added.Should().NotBeNull();
        invitationRepo.Added!.Status.Should().Be(InvitationStatus.Pending);
        invitationRepo.SaveChangesCalled.Should().BeTrue();
    }

    [Fact]
    public async Task Invitations_Create_Forbidden_Returns403()
    {
        var (userRepo, roleRepo, permRepo) = EmptyAdminRepos();
        using var server = await BuildAdminServerAsync(
            userRepo, roleRepo, permRepo, new FakePermissionAuthorizationService());
        using var client = AuthenticatedClient(server);

        var response = await client.PostAsJsonAsync(
            "/api/identity/invitations", new InvitationCreateDto("invitee@example.com", "Invitee Name"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Invitations_Delete_Authorized_RevokesAccountAndMarksRevoked_Returns204()
    {
        var invitation = NewInvitation(externalObjectId: "invitee-oid-1");
        var invitationProvider = new FakeUserInvitationProvider();
        var invitationRepo = new FakePendingInvitationRepository(
            new Dictionary<Guid, PendingInvitation> { [invitation.Id] = invitation });
        var (userRepo, roleRepo, permRepo) = EmptyAdminRepos();
        using var server = await BuildAdminServerAsync(
            userRepo, roleRepo, permRepo, new FakePermissionAuthorizationService("users:edit"),
            invitationRepo: invitationRepo, invitationProvider: invitationProvider);
        using var client = AuthenticatedClient(server);

        var response = await client.DeleteAsync($"/api/identity/invitations/{invitation.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        invitationProvider.RevokedExternalObjectId.Should().Be("invitee-oid-1");
        invitationRepo.Updated.Should().NotBeNull();
        invitationRepo.Updated!.Status.Should().Be(InvitationStatus.Revoked);
        invitationRepo.SaveChangesCalled.Should().BeTrue();
    }

    [Fact]
    public async Task Invitations_Delete_NotFound_Returns404()
    {
        var invitationProvider = new FakeUserInvitationProvider();
        var (userRepo, roleRepo, permRepo) = EmptyAdminRepos();
        using var server = await BuildAdminServerAsync(
            userRepo, roleRepo, permRepo, new FakePermissionAuthorizationService("users:edit"),
            invitationRepo: new FakePendingInvitationRepository(new Dictionary<Guid, PendingInvitation>()),
            invitationProvider: invitationProvider);
        using var client = AuthenticatedClient(server);

        var response = await client.DeleteAsync($"/api/identity/invitations/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        invitationProvider.RevokedExternalObjectId.Should().BeNull();
    }

    [Fact]
    public async Task Invitations_Delete_Forbidden_Returns403()
    {
        var (userRepo, roleRepo, permRepo) = EmptyAdminRepos();
        using var server = await BuildAdminServerAsync(
            userRepo, roleRepo, permRepo, new FakePermissionAuthorizationService());
        using var client = AuthenticatedClient(server);

        var response = await client.DeleteAsync($"/api/identity/invitations/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Invitations_Redeem_OwnInvitation_MarksRedeemedAndReturns204()
    {
        var caller = NewUser(externalObjectId: "caller-oid");
        var invitation = NewInvitation(externalObjectId: "caller-oid");
        var invitationRepo = new FakePendingInvitationRepository(
            new Dictionary<Guid, PendingInvitation> { [invitation.Id] = invitation });
        var (userRepo, roleRepo, permRepo) = EmptyAdminRepos();
        var authService = new FakeAuthorizationService(
            new AuthorizationContext { User = caller, Roles = [], Claims = [], Permissions = [] });
        using var server = await BuildAdminServerAsync(
            userRepo, roleRepo, permRepo, authService, invitationRepo: invitationRepo);
        using var client = server.CreateClient();

        var response = await client.PostAsync($"/api/identity/invitations/{invitation.Id}/redeem", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        invitationRepo.Updated.Should().NotBeNull();
        invitationRepo.Updated!.Status.Should().Be(InvitationStatus.Redeemed);
        invitationRepo.Updated.RedeemedAt.Should().NotBeNull();
        invitationRepo.SaveChangesCalled.Should().BeTrue();
    }

    [Fact]
    public async Task Invitations_Redeem_NotFound_Returns404()
    {
        var caller = NewUser(externalObjectId: "caller-oid");
        var (userRepo, roleRepo, permRepo) = EmptyAdminRepos();
        var authService = new FakeAuthorizationService(
            new AuthorizationContext { User = caller, Roles = [], Claims = [], Permissions = [] });
        using var server = await BuildAdminServerAsync(
            userRepo, roleRepo, permRepo, authService,
            invitationRepo: new FakePendingInvitationRepository(new Dictionary<Guid, PendingInvitation>()));
        using var client = server.CreateClient();

        var response = await client.PostAsync($"/api/identity/invitations/{Guid.NewGuid()}/redeem", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Invitations_Redeem_SomeoneElsesInvitation_Returns404AndDoesNotRedeem()
    {
        // Non-leakage: the caller must never redeem an invitation belonging to a different
        // ExternalObjectId, even when they supply that invitation's real id directly.
        var caller = NewUser(externalObjectId: "caller-oid");
        var othersInvitation = NewInvitation(externalObjectId: "someone-else-oid");
        var invitationRepo = new FakePendingInvitationRepository(
            new Dictionary<Guid, PendingInvitation> { [othersInvitation.Id] = othersInvitation });
        var (userRepo, roleRepo, permRepo) = EmptyAdminRepos();
        var authService = new FakeAuthorizationService(
            new AuthorizationContext { User = caller, Roles = [], Claims = [], Permissions = [] });
        using var server = await BuildAdminServerAsync(
            userRepo, roleRepo, permRepo, authService, invitationRepo: invitationRepo);
        using var client = server.CreateClient();

        var response = await client.PostAsync($"/api/identity/invitations/{othersInvitation.Id}/redeem", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        invitationRepo.Updated.Should().BeNull();
        othersInvitation.Status.Should().Be(InvitationStatus.Pending);
    }

    // ── User Claims (XD01-59 Phase 3, XD01-87) ──────────────────────────────

    private static UserClaim NewClaim(Guid userId, string type = "zone", string value = "north") => new()
    {
        Id     = Guid.NewGuid(),
        UserId = userId,
        Type   = type,
        Value  = value,
    };

    private static FakeAuthorizationService AuthServiceFor(AppUser caller) =>
        new(new AuthorizationContext { User = caller, Roles = [], Claims = [], Permissions = [] });

    [Fact]
    public async Task UserClaims_List_ReturnsOnlyTheCallersOwnClaims()
    {
        // Non-leakage: another user's claims must never appear in the caller's own list.
        var caller = NewUser();
        var ownClaim = NewClaim(caller.Id);
        var othersClaim = NewClaim(Guid.NewGuid());
        var claimRepo = new FakeUserClaimRepository(new Dictionary<Guid, List<UserClaim>>
        {
            [caller.Id]          = [ownClaim],
            [othersClaim.UserId] = [othersClaim],
        });
        var (userRepo, roleRepo, permRepo) = EmptyAdminRepos();
        using var server = await BuildAdminServerAsync(
            userRepo, roleRepo, permRepo, AuthServiceFor(caller), claimRepo: claimRepo);
        using var client = server.CreateClient();

        var claims = await client.GetFromJsonAsync<List<UserClaimDto>>("/api/identity/userclaims");

        claims.Should().ContainSingle(c => c.Id == ownClaim.Id);
    }

    [Fact]
    public async Task UserClaims_GetByType_Found_ReturnsClaim()
    {
        var caller = NewUser();
        var claim = NewClaim(caller.Id, type: "department", value: "operations");
        var claimRepo = new FakeUserClaimRepository(new Dictionary<Guid, List<UserClaim>> { [caller.Id] = [claim] });
        var (userRepo, roleRepo, permRepo) = EmptyAdminRepos();
        using var server = await BuildAdminServerAsync(
            userRepo, roleRepo, permRepo, AuthServiceFor(caller), claimRepo: claimRepo);
        using var client = server.CreateClient();

        var dto = await client.GetFromJsonAsync<UserClaimDto>("/api/identity/userclaims/department");

        dto.Should().NotBeNull();
        dto!.Value.Should().Be("operations");
    }

    [Fact]
    public async Task UserClaims_GetByType_NotFound_Returns404()
    {
        var caller = NewUser();
        var claimRepo = new FakeUserClaimRepository(new Dictionary<Guid, List<UserClaim>>());
        var (userRepo, roleRepo, permRepo) = EmptyAdminRepos();
        using var server = await BuildAdminServerAsync(
            userRepo, roleRepo, permRepo, AuthServiceFor(caller), claimRepo: claimRepo);
        using var client = server.CreateClient();

        var response = await client.GetAsync("/api/identity/userclaims/department");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UserClaims_Create_AddsForTheCallerAndReturns201()
    {
        var caller = NewUser();
        var claimRepo = new FakeUserClaimRepository(new Dictionary<Guid, List<UserClaim>>());
        var (userRepo, roleRepo, permRepo) = EmptyAdminRepos();
        using var server = await BuildAdminServerAsync(
            userRepo, roleRepo, permRepo, AuthServiceFor(caller), claimRepo: claimRepo);
        using var client = server.CreateClient();

        var response = await client.PostAsJsonAsync("/api/identity/userclaims", new UserClaimWriteDto("phone", "+1-555-0100"));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        claimRepo.Added.Should().NotBeNull();
        claimRepo.Added!.UserId.Should().Be(caller.Id);
        claimRepo.Added.Type.Should().Be("phone");
        claimRepo.Added.Value.Should().Be("+1-555-0100");
        claimRepo.SaveChangesCalled.Should().BeTrue();
    }

    [Fact]
    public async Task UserClaims_Update_OwnClaim_UpdatesValueAndReturns204()
    {
        var caller = NewUser();
        var claim = NewClaim(caller.Id, value: "old-value");
        var claimRepo = new FakeUserClaimRepository(new Dictionary<Guid, List<UserClaim>> { [caller.Id] = [claim] });
        var (userRepo, roleRepo, permRepo) = EmptyAdminRepos();
        using var server = await BuildAdminServerAsync(
            userRepo, roleRepo, permRepo, AuthServiceFor(caller), claimRepo: claimRepo);
        using var client = server.CreateClient();

        var response = await client.PutAsJsonAsync($"/api/identity/userclaims/{claim.Id}", new UserClaimUpdateDto("new-value"));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        claimRepo.Updated.Should().NotBeNull();
        claimRepo.Updated!.Value.Should().Be("new-value");
        claimRepo.SaveChangesCalled.Should().BeTrue();
    }

    [Fact]
    public async Task UserClaims_Update_UnknownClaimId_Returns404()
    {
        var caller = NewUser();
        var claimRepo = new FakeUserClaimRepository(new Dictionary<Guid, List<UserClaim>>());
        var (userRepo, roleRepo, permRepo) = EmptyAdminRepos();
        using var server = await BuildAdminServerAsync(
            userRepo, roleRepo, permRepo, AuthServiceFor(caller), claimRepo: claimRepo);
        using var client = server.CreateClient();

        var response = await client.PutAsJsonAsync($"/api/identity/userclaims/{Guid.NewGuid()}", new UserClaimUpdateDto("value"));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UserClaims_Update_SomeoneElsesClaim_Returns404AndDoesNotUpdate()
    {
        // Non-leakage: the caller must never update another user's claim by supplying its real id.
        var caller = NewUser();
        var othersClaim = NewClaim(Guid.NewGuid(), value: "untouched");
        var claimRepo = new FakeUserClaimRepository(new Dictionary<Guid, List<UserClaim>> { [othersClaim.UserId] = [othersClaim] });
        var (userRepo, roleRepo, permRepo) = EmptyAdminRepos();
        using var server = await BuildAdminServerAsync(
            userRepo, roleRepo, permRepo, AuthServiceFor(caller), claimRepo: claimRepo);
        using var client = server.CreateClient();

        var response = await client.PutAsJsonAsync($"/api/identity/userclaims/{othersClaim.Id}", new UserClaimUpdateDto("hacked"));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        claimRepo.Updated.Should().BeNull();
        othersClaim.Value.Should().Be("untouched");
    }

    [Fact]
    public async Task UserClaims_Delete_OwnClaim_RemovesAndReturns204()
    {
        var caller = NewUser();
        var claim = NewClaim(caller.Id);
        var claimRepo = new FakeUserClaimRepository(new Dictionary<Guid, List<UserClaim>> { [caller.Id] = [claim] });
        var (userRepo, roleRepo, permRepo) = EmptyAdminRepos();
        using var server = await BuildAdminServerAsync(
            userRepo, roleRepo, permRepo, AuthServiceFor(caller), claimRepo: claimRepo);
        using var client = server.CreateClient();

        var response = await client.DeleteAsync($"/api/identity/userclaims/{claim.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        claimRepo.Removed.Should().Be(claim.Id);
        claimRepo.SaveChangesCalled.Should().BeTrue();
    }

    [Fact]
    public async Task UserClaims_Delete_UnknownClaimId_Returns404()
    {
        var caller = NewUser();
        var claimRepo = new FakeUserClaimRepository(new Dictionary<Guid, List<UserClaim>>());
        var (userRepo, roleRepo, permRepo) = EmptyAdminRepos();
        using var server = await BuildAdminServerAsync(
            userRepo, roleRepo, permRepo, AuthServiceFor(caller), claimRepo: claimRepo);
        using var client = server.CreateClient();

        var response = await client.DeleteAsync($"/api/identity/userclaims/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UserClaims_Delete_SomeoneElsesClaim_Returns404AndDoesNotRemove()
    {
        // Non-leakage: the caller must never delete another user's claim by supplying its real id.
        var caller = NewUser();
        var othersClaim = NewClaim(Guid.NewGuid());
        var claimRepo = new FakeUserClaimRepository(new Dictionary<Guid, List<UserClaim>> { [othersClaim.UserId] = [othersClaim] });
        var (userRepo, roleRepo, permRepo) = EmptyAdminRepos();
        using var server = await BuildAdminServerAsync(
            userRepo, roleRepo, permRepo, AuthServiceFor(caller), claimRepo: claimRepo);
        using var client = server.CreateClient();

        var response = await client.DeleteAsync($"/api/identity/userclaims/{othersClaim.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        claimRepo.Removed.Should().BeNull();
    }

    [Fact]
    public async Task UserClaims_DeleteAll_RemovesAllForTheCallerAndReturns204()
    {
        var caller = NewUser();
        var claimRepo = new FakeUserClaimRepository(new Dictionary<Guid, List<UserClaim>>());
        var (userRepo, roleRepo, permRepo) = EmptyAdminRepos();
        using var server = await BuildAdminServerAsync(
            userRepo, roleRepo, permRepo, AuthServiceFor(caller), claimRepo: claimRepo);
        using var client = server.CreateClient();

        var response = await client.DeleteAsync("/api/identity/userclaims");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        claimRepo.RemovedAllForUserId.Should().Be(caller.Id);
        claimRepo.SaveChangesCalled.Should().BeTrue();
    }
}
