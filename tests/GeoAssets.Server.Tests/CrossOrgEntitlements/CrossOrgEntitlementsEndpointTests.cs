using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;
using GeoAssets.Identity.Authorization.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace GeoAssets.Server.Tests.CrossOrgEntitlements;

/// <summary>
/// XD01-329 — <c>GET /api/identity/me/cross-org-entitlements</c>: the caller's own entitlements on other organizations — its
/// organization's active grants and the roles it holds on other organizations, each organization named — and never another
/// user's. Authenticated only, through the host's fallback policy as in <c>AddGeoAssetsAuthentication</c>.
/// </summary>
public sealed class CrossOrgEntitlementsEndpointTests
{
    private const string UserHeader = "X-Test-User";
    private const string View = "serviceorders:view";
    private const string Execute = "serviceorders:execute";

    private static readonly Guid Coelba = new("c0e1ba00-0000-4000-8000-000000000329");
    private static readonly Guid Bimbo = new("b1b00000-0000-4000-8000-000000000329");
    private static readonly Guid Third = new("7e1d0000-0000-4000-8000-000000000329");
    private static readonly Guid BimboTechnician = new("00000000-0000-4000-8000-0000000b1b00");
    private static readonly Guid CoelbaPlanner = new("00000000-0000-4000-8000-0000000c0e1b");
    private static readonly Guid Unaffiliated = new("00000000-0000-4000-8000-000000000000");
    private static readonly Uri Route = new("/api/identity/me/cross-org-entitlements", UriKind.Relative);
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);
    private static readonly DateTime ExpiresAt = new(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>The kernel's per-request authorization service, answering for whichever user the request is from.</summary>
    private sealed class PerCallerAuthorizationService(IHttpContextAccessor http) : IGeoAuthorizationService
    {
        public Task<bool> IsInRoleAsync(string roleName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> HasClaimAsync(string claimType, string? claimValue = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> HasPermissionAsync(string permissionCode, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> EvaluatePolicyAsync(string policyName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> EvaluatePolicyAsync(AppPolicy policy, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<AuthorizationContext> GetAuthorizationContextAsync(CancellationToken ct = default)
        {
            var userId = Guid.Parse(http.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            return Task.FromResult(userId == BimboTechnician ? Technician()
                : userId == CoelbaPlanner ? Planner()
                : Context(Unaffiliated, organizationId: null, scopedRoles: new() { [Coelba] = ["Contractor C1"] }, scopedPermissions: new() { [Coelba] = [View] }));
        }

        private static AuthorizationContext Technician() => Context(
            BimboTechnician, Bimbo,
            scopedRoles: new() { [Coelba] = ["Contractor C1"] },
            scopedPermissions: new() { [Coelba] = [View, Execute] });

        private static AuthorizationContext Planner() => Context(
            CoelbaPlanner, Coelba,
            scopedRoles: new() { [Third] = ["Auditor"] },
            scopedPermissions: new() { [Third] = ["serviceorders:read"] });

        private static AuthorizationContext Context(
            Guid userId, Guid? organizationId,
            Dictionary<Guid, IReadOnlyList<string>> scopedRoles, Dictionary<Guid, IReadOnlyList<string>> scopedPermissions) => new()
        {
            User = new AppUser
            {
                Id = userId,
                Email = $"{userId}@example.com",
                DisplayName = "User",
                OrganizationId = organizationId,
                CreatedAt = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
            },
            Roles = ["Technician"],
            Claims = [],
            Permissions = ["serviceorders:read"],
            ScopedRoles = scopedRoles,
            ScopedPermissions = scopedPermissions,
        };
    }

    private sealed class NamedOrganizations : IOrganizationRepository
    {
        private static readonly Dictionary<Guid, string> Names = new() { [Coelba] = "COELBA", [Bimbo] = "BIMBO", [Third] = "THIRD" };

        public Task<Organization?> GetByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult(
            Names.TryGetValue(id, out var name)
                ? new Organization { Id = id, Name = name, Slug = name, CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) }
                : null);

        public Task<Organization?> GetBySlugAsync(string slug, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<Organization>> GetAllAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<AppUser>> GetUsersAsync(Guid organizationId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task AddAsync(Organization organization, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpdateAsync(Organization organization, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveChangesAsync(CancellationToken ct = default) => throw new NotSupportedException();
    }

    private static async Task<TestServer> BuildServerAsync()
    {
        var grants = new TestOrganizationGrantRepository();
        grants.Active.Add(new OrganizationGrant
        {
            GranteeOrganizationId = Bimbo,
            ResourceOrganizationId = Coelba,
            ResourceType = "ServiceOrder",
            ScopeId = "municipio-123",
            AllowedActions = [View, Execute],
            RequiredRole = "Contractor C1",
            RequiresScopedRole = true,
            OrderTypeIds = ["cut"],
            ExpiresAt = ExpiresAt,
            GrantedBy = "coelba-admin",
        });
        grants.Active.Add(new OrganizationGrant
        {
            GranteeOrganizationId = Coelba,
            ResourceOrganizationId = Third,
            ResourceType = "Feature",
            AllowedActions = ["features:reference"],
            GrantedBy = "third-admin",
        });

        var host = await new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddHttpContextAccessor();
                    services.AddAuthentication("Test")
                        .AddScheme<AuthenticationSchemeOptions, NoOpAuthenticationHandler>("Test", _ => { });
                    services.AddAuthorization(options =>
                        options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
                    services.AddScoped<IGeoAuthorizationService, PerCallerAuthorizationService>();
                    services.AddSingleton<IOrganizationGrantRepository>(grants);
                    services.AddSingleton<IOrganizationRepository, NamedOrganizations>();
                });
                webHost.Configure(app =>
                {
                    app.Use(async (ctx, next) =>
                    {
                        if (ctx.Request.Headers.TryGetValue(UserHeader, out var user))
                            ctx.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user.ToString())], "TestScheme"));
                        await next();
                    });
                    app.UseRouting();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints => endpoints.MapCrossOrgEntitlementsApi());
                });
            })
            .StartAsync();

        return host.GetTestServer();
    }

    private static async Task<(HttpResponseMessage Response, string Body)> GetAsAsync(TestServer server, Guid? user, Uri? route = null)
    {
        using var client = server.CreateClient();
        if (user is { } id) client.DefaultRequestHeaders.Add(UserHeader, id.ToString());
        var response = await client.GetAsync(route ?? Route);
        return (response, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task XD01_329_ACallerGetsItsOrganizationsGrants_AndItsScopedRoles_EachOrganizationNamed()
    {
        using var server = await BuildServerAsync();

        var (response, body) = await GetAsAsync(server, BimboTechnician);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(body);
        var grant = json.RootElement.GetProperty("grants").EnumerateArray().Should().ContainSingle().Subject;
        grant.GetProperty("resourceOrganizationId").GetGuid().Should().Be(Coelba);
        grant.GetProperty("resourceOrganizationName").GetString().Should().Be("COELBA");
        grant.GetProperty("resourceType").GetString().Should().Be("ServiceOrder");
        grant.GetProperty("scopeId").GetString().Should().Be("municipio-123");
        grant.GetProperty("orderTypeIds").EnumerateArray().Select(e => e.GetString()).Should().Equal("cut");
        grant.GetProperty("allowedActions").EnumerateArray().Select(e => e.GetString()).Should().Equal(View, Execute);
        grant.GetProperty("requiredRole").GetString().Should().Be("Contractor C1");
        grant.GetProperty("requiresScopedRole").GetBoolean().Should().BeTrue();
        grant.GetProperty("expiresAt").GetDateTime().Should().Be(ExpiresAt);

        var scoped = json.RootElement.GetProperty("scopedRoles").EnumerateArray().Should().ContainSingle().Subject;
        scoped.GetProperty("organizationId").GetGuid().Should().Be(Coelba);
        scoped.GetProperty("organizationName").GetString().Should().Be("COELBA");
        scoped.GetProperty("roles").EnumerateArray().Select(e => e.GetString()).Should().Equal("Contractor C1");
        scoped.GetProperty("permissions").EnumerateArray().Select(e => e.GetString()).Should().Equal(View, Execute);

        body.Should().NotContain("coelba-admin", "who granted it is not the grantee's to read");
    }

    [Fact]
    public async Task XD01_329_TwoUsersOfDifferentOrganizations_EachGetOnlyTheirOwn()
    {
        using var server = await BuildServerAsync();

        var (_, technicianBody) = await GetAsAsync(server, BimboTechnician);
        var (_, plannerBody) = await GetAsAsync(server, CoelbaPlanner);

        var technician = JsonSerializer.Deserialize<CrossOrgEntitlementsDto>(technicianBody, WebJson)!;
        var planner = JsonSerializer.Deserialize<CrossOrgEntitlementsDto>(plannerBody, WebJson)!;

        technician.Grants.Select(g => g.ResourceOrganizationId).Should().Equal(Coelba);
        technician.ScopedRoles.Select(s => s.OrganizationId).Should().Equal(Coelba);
        technicianBody.Should().NotContain(Third.ToString()).And.NotContain("features:reference").And.NotContain("Auditor");

        planner.Grants.Should().ContainSingle().Which.Should().Match<CrossOrgGrantEntitlementDto>(g =>
            g.ResourceOrganizationId == Third && g.ResourceOrganizationName == "THIRD" && g.AllowedActions.SequenceEqual(new[] { "features:reference" }));
        planner.ScopedRoles.Should().ContainSingle().Which.Roles.Should().Equal("Auditor");
        plannerBody.Should().NotContain("Contractor C1").And.NotContain("municipio-123");
    }

    [Fact]
    public async Task XD01_329_TheCallerComesFromItsToken_NeverFromTheRequest()
    {
        using var server = await BuildServerAsync();

        var (_, body) = await GetAsAsync(server, BimboTechnician,
            new Uri($"{Route}?userId={CoelbaPlanner}&organizationId={Coelba}", UriKind.Relative));

        body.Should().NotContain("THIRD").And.NotContain("Auditor");
    }

    [Fact]
    public async Task XD01_329_Anonymous_Gets401()
    {
        using var server = await BuildServerAsync();

        var (response, body) = await GetAsAsync(server, user: null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        body.Should().NotContain("COELBA");
    }

    [Fact]
    public async Task XD01_329_ACallerWithNoOrganization_HoldsNoGrant_ButItsScopedRolesStillCount()
    {
        using var server = await BuildServerAsync();

        var (response, body) = await GetAsAsync(server, Unaffiliated);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var read = JsonSerializer.Deserialize<CrossOrgEntitlementsDto>(body, WebJson)!;
        read.Grants.Should().BeEmpty();
        read.ScopedRoles.Should().ContainSingle().Which.OrganizationName.Should().Be("COELBA");
    }

    [Fact]
    public async Task XD01_329_ReadBackTheWayTheWebClientReadsIt_TheGrantsAndScopedMapsRoundTrip()
    {
        using var server = await BuildServerAsync();
        using var client = server.CreateClient();
        client.DefaultRequestHeaders.Add(UserHeader, BimboTechnician.ToString());

        var read = (await client.GetFromJsonAsync<CrossOrgEntitlementsDto>(Route))!;

        var grant = read.ToGrants(Bimbo).Should().ContainSingle().Subject;
        grant.GranteeOrganizationId.Should().Be(Bimbo);
        grant.RequiresScopedRole.Should().BeTrue();
        grant.OrderTypeIds.Should().Equal("cut");
        grant.IsActive.Should().BeTrue();
        read.ToScopedPermissions()[Coelba].Should().Equal(View, Execute);
        read.ToScopedRoles()[Coelba].Should().Equal("Contractor C1");
        read.OrganizationName(Coelba).Should().Be("COELBA");
        read.OrganizationName(Third).Should().BeNull();
    }
}
