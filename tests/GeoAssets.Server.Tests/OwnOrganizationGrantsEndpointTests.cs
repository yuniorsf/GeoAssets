using System.Net;
using System.Text.Json;
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

/// <summary>
/// XD01-332 — <c>GET /api/identity/organization-grants/me</c>, the self-service read a client
/// uses to evaluate the service-order organization gate as the server does: the active grants
/// held by the caller's OWN organization, and nothing a caller could steer.
/// </summary>
public class OwnOrganizationGrantsEndpointTests
{
    private static readonly Guid OrgA = new("a0a0a0a0-0000-4000-8000-000000000332");
    private static readonly Guid OrgB = new("b0b0b0b0-0000-4000-8000-000000000332");
    private static readonly Uri Route = new("/api/identity/organization-grants/me", UriKind.Relative);

    private sealed class FixedAuthorizationService(Guid? organizationId) : IGeoAuthorizationService
    {
        public Task<bool> IsInRoleAsync(string roleName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> HasClaimAsync(string claimType, string? claimValue = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> HasPermissionAsync(string permissionCode, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> EvaluatePolicyAsync(string policyName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> EvaluatePolicyAsync(AppPolicy policy, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<AuthorizationContext> GetAuthorizationContextAsync(CancellationToken ct = default) => Task.FromResult(new AuthorizationContext
        {
            User = new AppUser
            {
                Id = Guid.NewGuid(),
                Email = "user@example.com",
                DisplayName = "User",
                OrganizationId = organizationId,
                CreatedAt = new DateTime(2026, 11, 16, 0, 0, 0, DateTimeKind.Utc),
            },
            Roles = [],
            Claims = [],
            Permissions = [], // the route needs no permission: it is the caller's own data
        });
    }

    private static async Task<(TestServer Server, TestOrganizationGrantRepository Grants)> BuildServerAsync(Guid? callerOrganizationId)
    {
        var grants = new TestOrganizationGrantRepository();
        var host = await new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddSingleton<IGeoAuthorizationService>(new FixedAuthorizationService(callerOrganizationId));
                    services.AddSingleton<IOrganizationGrantRepository>(grants);
                });
                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapOwnOrganizationGrantsApi());
                });
            })
            .StartAsync();

        return (host.GetTestServer(), grants);
    }

    private static OrganizationGrant Grant(Guid grantee, Guid resource, string? scopeId = null) => new()
    {
        GranteeOrganizationId = grantee,
        ResourceOrganizationId = resource,
        ResourceType = "ServiceOrder",
        ScopeId = scopeId,
        AllowedActions = { "serviceorders:view" },
        RequiredRole = "Supervisor",
        ExpiresAt = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        GrantedBy = "an-administrator-of-a",
    };

    [Fact]
    public async Task XD01_332_OwnGrants_ReturnsTheActiveGrantsOfTheCallersOwnOrganization_AndNoOtherOrganizations()
    {
        var (server, grants) = await BuildServerAsync(OrgB);
        using var _ = server;
        grants.Active.Add(Grant(grantee: OrgB, resource: OrgA, scopeId: "municipio-123"));
        grants.Active.Add(Grant(grantee: OrgA, resource: OrgB)); // held by the other organization
        using var client = server.CreateClient();

        var response = await client.GetAsync(Route);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var text = await response.Content.ReadAsStringAsync();
        var items = JsonDocument.Parse(text).RootElement;
        items.GetArrayLength().Should().Be(1);
        var item = items[0];
        item.GetProperty("resourceOrganizationId").GetGuid().Should().Be(OrgA);
        item.GetProperty("resourceType").GetString().Should().Be("ServiceOrder");
        item.GetProperty("scopeId").GetString().Should().Be("municipio-123");
        item.GetProperty("allowedActions").EnumerateArray().Select(a => a.GetString()).Should().Equal("serviceorders:view");
        item.GetProperty("requiredRole").GetString().Should().Be("Supervisor");
        item.GetProperty("expiresAt").GetDateTime().Should().Be(new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        text.Should().NotContain("an-administrator-of-a", "who granted it is not the grantee's to read");
    }

    [Fact]
    public async Task XD01_332_OwnGrants_TheOrganizationComesFromTheCaller_NeverFromTheRequest()
    {
        var (server, grants) = await BuildServerAsync(OrgB);
        using var _ = server;
        grants.Active.Add(Grant(grantee: OrgA, resource: OrgB));
        using var client = server.CreateClient();

        var response = await client.GetAsync(new Uri($"{Route}?organizationId={OrgA}&granteeOrganizationId={OrgA}", UriKind.Relative));

        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task XD01_332_OwnGrants_CallerWithNoOrganization_GetsAnEmptyList_Not404()
    {
        var (server, grants) = await BuildServerAsync(callerOrganizationId: null);
        using var _ = server;
        grants.Active.Add(Grant(grantee: OrgB, resource: OrgA));
        using var client = server.CreateClient();

        var response = await client.GetAsync(Route);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetArrayLength().Should().Be(0);
    }
}
