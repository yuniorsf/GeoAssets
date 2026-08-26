using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;
using GeoAssets.Identity.Authorization.Services;
using GeoAssets.Provider.InMemory;
using GeoAssets.Provider.PostgreSQL.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace GeoAssets.Server.Tests;

/// <summary>
/// Never actually invoked by these tests — WMS authorization gating happens in middleware
/// before the endpoint delegate (and therefore <see cref="WmsPostGisRenderer"/>) runs. Only
/// exists so the "/wms" endpoint's delegate metadata can be built at all: minimal APIs infer
/// a complex, unregistered parameter type as a request body, which throws immediately for a
/// GET endpoint — registering a (non-functional) factory avoids that build-time failure.
/// </summary>
internal sealed class NeverCalledDbContextFactory : IDbContextFactory<GeoAssetsDbContext>
{
    public GeoAssetsDbContext CreateDbContext() => throw new NotSupportedException();
    public Task<GeoAssetsDbContext> CreateDbContextAsync(CancellationToken ct = default) => throw new NotSupportedException();
}

/// <summary>
/// Never actually invoked by tests that only exercise the subject-only gate — registered
/// purely because <c>OrgResourceAuthorizationHandler</c> (XD01-21) is now unconditionally
/// constructed by <c>DefaultAuthorizationHandlerProvider</c> for every authorization check,
/// resource-based or not, so its constructor dependencies must resolve even when a given
/// test never reaches a resource-aware check.
/// </summary>
internal sealed class NeverCalledOrganizationGrantRepository : IOrganizationGrantRepository
{
    public Task<OrganizationGrant?> GetByIdAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<OrganizationGrant>> GetAllAsync(CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<OrganizationGrant>> GetActiveGrantsAsync(Guid granteeOrganizationId, Guid resourceOrganizationId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<OrganizationGrant>> GetActiveGrantsForGranteeAsync(Guid granteeOrganizationId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task AddAsync(OrganizationGrant grant, CancellationToken ct = default) => throw new NotSupportedException();
    public Task UpdateAsync(OrganizationGrant grant, CancellationToken ct = default) => throw new NotSupportedException();
    public Task SaveChangesAsync(CancellationToken ct = default) => throw new NotSupportedException();
}

/// <summary>
/// Proves the real production endpoints in <c>GeoAssetsRestApiExtensions</c>/
/// <c>WfsEndpointExtensions</c>/<c>WmsEndpointExtensions</c> are actually gated by the
/// permission codes documented on each (XD01-15) — not just that the AuthZ bridge works in
/// isolation (see <see cref="GeoAuthorizationPolicyBridgeEndToEndTests"/>).
/// </summary>
public class EndpointAuthorizationTests
{
    private sealed class FakeAuthorizationService(HashSet<string> grantedPermissions) : IGeoAuthorizationService
    {
        public Task<bool> IsInRoleAsync(string roleName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> HasClaimAsync(string claimType, string? claimValue = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> HasPermissionAsync(string permissionCode, CancellationToken ct = default)
            => Task.FromResult(grantedPermissions.Contains(permissionCode));
        public Task<bool> EvaluatePolicyAsync(string policyName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> EvaluatePolicyAsync(AppPolicy policy, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AuthorizationContext> GetAuthorizationContextAsync(CancellationToken ct = default) => throw new NotSupportedException();
    }

    private static Task<TestServer> BuildServerAsync(params string[] grantedPermissions) =>
        BuildServerAsync(wmsRequireAuthentication: true, grantedPermissions);

    private static async Task<TestServer> BuildServerAsync(bool wmsRequireAuthentication, params string[] grantedPermissions)
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
                    services.AddSingleton<IGeoAuthorizationService>(new FakeAuthorizationService([.. grantedPermissions]));
                    services.AddSingleton<IOrganizationGrantRepository, NeverCalledOrganizationGrantRepository>();
                    services.AddSingleton<IAssetProvider>(new InMemoryAssetProvider());
                    services.AddSingleton<IDbContextFactory<GeoAssetsDbContext>, NeverCalledDbContextFactory>();
                    services.AddSingleton<WmsPostGisRenderer>();
                });
                webHost.Configure(app =>
                {
                    // Stand-in for real bearer-token authentication (XD01-12).
                    app.Use(async (ctx, next) =>
                    {
                        if (ctx.Request.Headers.ContainsKey("X-Test-Authenticated"))
                            ctx.User = new ClaimsPrincipal(new ClaimsIdentity(
                                [new Claim(ClaimTypes.NameIdentifier, "test-user")], "TestScheme"));
                        await next();
                    });
                    app.UseRouting();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGeoAssetsApi();
                        endpoints.MapWfsApi();
                        endpoints.MapWmsApi(requireAuthentication: wmsRequireAuthentication);
                    });
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

    // ── GET /api/geoassets/features (features:read) ──────────────────────────

    [Fact]
    public async Task GetFeatures_Unauthenticated_Returns401()
    {
        using var server = await BuildServerAsync("features:read");
        using var client = server.CreateClient();

        var response = await client.GetAsync("/api/geoassets/features");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetFeatures_MissingReadPermission_Returns403()
    {
        using var server = await BuildServerAsync(); // authenticated, no permissions granted
        using var client = AuthenticatedClient(server);

        var response = await client.GetAsync("/api/geoassets/features");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetFeatures_HasReadPermission_Returns200()
    {
        using var server = await BuildServerAsync("features:read");
        using var client = AuthenticatedClient(server);

        var response = await client.GetAsync("/api/geoassets/features");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── POST /api/geoassets/features (features:edit — not features:read) ─────

    [Fact]
    public async Task PostFeatures_OnlyHasReadPermission_Returns403()
    {
        // Non-leakage: features:read must not also unlock write access.
        using var server = await BuildServerAsync("features:read");
        using var client = AuthenticatedClient(server);
        var body = new StringContent("""{"properties":{"name":"Test"}}""", Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/api/geoassets/features", body);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PostFeatures_HasEditPermission_Returns201()
    {
        using var server = await BuildServerAsync("features:edit");
        using var client = AuthenticatedClient(server);
        var body = new StringContent("""{"properties":{"name":"Test"}}""", Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/api/geoassets/features", body);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    // ── DELETE /api/geoassets/features/{id} (features:delete — not features:edit) ─

    [Fact]
    public async Task DeleteFeature_OnlyHasEditPermission_Returns403()
    {
        // Non-leakage: features:edit must not also unlock delete access.
        using var server = await BuildServerAsync("features:edit");
        using var client = AuthenticatedClient(server);

        var response = await client.DeleteAsync("/api/geoassets/features/some-id");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DeleteFeature_HasDeletePermission_Returns204()
    {
        using var server = await BuildServerAsync("features:delete");
        using var client = AuthenticatedClient(server);

        var response = await client.DeleteAsync("/api/geoassets/features/some-id");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // ── GET /wfs (features:read) ──────────────────────────────────────────────

    [Fact]
    public async Task Wfs_Unauthenticated_Returns401()
    {
        using var server = await BuildServerAsync("features:read");
        using var client = server.CreateClient();

        var response = await client.GetAsync("/wfs?SERVICE=WFS&VERSION=2.0.0&REQUEST=GetCapabilities");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Wfs_HasReadPermission_Returns200()
    {
        using var server = await BuildServerAsync("features:read");
        using var client = AuthenticatedClient(server);

        var response = await client.GetAsync("/wfs?SERVICE=WFS&VERSION=2.0.0&REQUEST=GetCapabilities");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── GET /wms (features:read) — capabilities only; GetMap needs a real Postgres-backed
    // WmsPostGisRenderer, out of scope for this authorization-gating test ─────────────────

    [Fact]
    public async Task Wms_Unauthenticated_Returns401()
    {
        using var server = await BuildServerAsync("features:read");
        using var client = server.CreateClient();

        var response = await client.GetAsync("/wms?SERVICE=WMS&VERSION=1.1.1&REQUEST=GetCapabilities");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Wms_MissingReadPermission_Returns403()
    {
        using var server = await BuildServerAsync(); // authenticated, no permissions granted
        using var client = AuthenticatedClient(server);

        var response = await client.GetAsync("/wms?SERVICE=WMS&VERSION=1.1.1&REQUEST=GetCapabilities");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Wms_RequireAuthenticationDisabled_AllowsAnonymousAccess()
    {
        // Interim toggle (XD01-95): the in-app Leaflet map can't attach a bearer token to its
        // <img>-based tile requests, so Wms:RequireAuthentication=false must actually let an
        // anonymous caller reach the handler, not just change the default. An unsupported
        // REQUEST value needs no database access (unlike GetCapabilities/GetMap/GetFeatureInfo —
        // out of scope for this authorization-gating test, see the class comment above), so a
        // 400 here proves the request got past authorization; a 401/403 would mean the flag had
        // no effect.
        using var server = await BuildServerAsync(wmsRequireAuthentication: false);
        using var client = server.CreateClient();

        var response = await client.GetAsync("/wms?SERVICE=WMS&VERSION=1.1.1&REQUEST=Nonsense");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
