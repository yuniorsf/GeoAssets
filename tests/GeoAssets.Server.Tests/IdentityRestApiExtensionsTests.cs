using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;
using GeoAssets.Identity.Authorization.Services;
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
}
