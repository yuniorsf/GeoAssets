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

    private static AppUser NewUser(string email = "user@example.com") => new()
    {
        Id          = Guid.NewGuid(),
        Email       = email,
        DisplayName = "Test User",
        CreatedAt   = DateTime.UtcNow,
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
    /// authorized/forbidden split for each endpoint test below.</summary>
    private sealed class FakePermissionAuthorizationService(params string[] grantedCodes) : IGeoAuthorizationService
    {
        public Task<bool> IsInRoleAsync(string roleName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> HasClaimAsync(string claimType, string? claimValue = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> HasPermissionAsync(string permissionCode, CancellationToken ct = default) => Task.FromResult(grantedCodes.Contains(permissionCode));
        public Task<bool> EvaluatePolicyAsync(string policyName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> EvaluatePolicyAsync(AppPolicy policy, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AuthorizationContext> GetAuthorizationContextAsync(CancellationToken ct = default) => throw new NotSupportedException();
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

    /// <summary>
    /// Real ASP.NET Core auth pipeline (AddGeoAuthorizationPolicyBridge + a no-op scheme),
    /// mirroring GeoAuthorizationPolicyBridgeEndToEndTests — needed because, unlike /me and
    /// /policies, the admin endpoints call .RequireAuthorization("resource:action").
    /// </summary>
    private static async Task<TestServer> BuildAdminServerAsync(
        IUserRepository userRepo, IRoleRepository roleRepo, IPermissionRepository permissionRepo,
        IGeoAuthorizationService authService, IRoleAssignmentProvider? roleSync = null)
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
}
