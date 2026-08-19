using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
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
/// Proves <see cref="OrgResourceAuthorizationHandler"/> (XD01-21) is actually wired into the
/// real <c>GET/PUT/DELETE /features/{id}</c> and <c>DELETE /asset-types/{id}</c> endpoints in
/// <c>GeoAssetsRestApiExtensions</c> — not just that the handler is correct in isolation (see
/// <see cref="OrgResourceAuthorizationHandlerTests"/>).
/// </summary>
public class OrgResourceEndpointAuthorizationTests
{
    private sealed class FakeAuthorizationService(Guid? userOrganizationId) : IGeoAuthorizationService
    {
        public Task<bool> IsInRoleAsync(string roleName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> HasClaimAsync(string claimType, string? claimValue = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> HasPermissionAsync(string permissionCode, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> EvaluatePolicyAsync(string policyName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> EvaluatePolicyAsync(AppPolicy policy, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<AuthorizationContext> GetAuthorizationContextAsync(CancellationToken ct = default)
            => Task.FromResult(new AuthorizationContext
            {
                User = new AppUser
                {
                    Id = Guid.NewGuid(), Email = "test@example.com", DisplayName = "Test",
                    CreatedAt = DateTime.UtcNow, OrganizationId = userOrganizationId,
                },
                Roles       = [],
                Claims      = [],
                Permissions = [],
            });
    }

    private sealed class FakeOrganizationGrantRepository(IReadOnlyList<OrganizationGrant> grants)
        : IOrganizationGrantRepository
    {
        public Task<OrganizationGrant?> GetByIdAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<OrganizationGrant>> GetAllAsync(CancellationToken ct = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<OrganizationGrant>> GetActiveGrantsAsync(
            Guid granteeOrganizationId, Guid resourceOrganizationId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<OrganizationGrant>>(
                [.. grants.Where(g => g.GranteeOrganizationId == granteeOrganizationId
                                    && g.ResourceOrganizationId == resourceOrganizationId)]);

        public Task<IReadOnlyList<OrganizationGrant>> GetActiveGrantsForGranteeAsync(
            Guid granteeOrganizationId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<OrganizationGrant>>(
                [.. grants.Where(g => g.GranteeOrganizationId == granteeOrganizationId)]);

        public Task AddAsync(OrganizationGrant grant, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpdateAsync(OrganizationGrant grant, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveChangesAsync(CancellationToken ct = default) => throw new NotSupportedException();
    }

    private static async Task<(TestServer Server, IAssetProvider Provider)> BuildServerAsync(
        Guid? userOrganizationId, IReadOnlyList<OrganizationGrant>? grants = null)
    {
        var provider = new InMemoryAssetProvider();

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
                    services.AddSingleton<IGeoAuthorizationService>(new FakeAuthorizationService(userOrganizationId));
                    services.AddSingleton<IOrganizationGrantRepository>(new FakeOrganizationGrantRepository(grants ?? []));
                    services.AddSingleton<IAssetProvider>(provider);
                    services.AddSingleton<IDbContextFactory<GeoAssetsDbContext>, NeverCalledDbContextFactory>();
                    services.AddSingleton<WmsPostGisRenderer>();
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
                    app.UseEndpoints(endpoints => endpoints.MapGeoAssetsApi());
                });
            })
            .StartAsync();

        return (host.GetTestServer(), provider);
    }

    private static HttpClient AuthenticatedClient(TestServer server)
    {
        var client = server.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Authenticated", "1");
        return client;
    }

    // ── GET /features/{id} ────────────────────────────────────────────────────

    [Fact]
    public async Task GetFeature_DifferentOrganizationNoGrant_Returns403()
    {
        var resourceOrgId = Guid.NewGuid();
        var (server, provider) = await BuildServerAsync(userOrganizationId: Guid.NewGuid());
        var feature = new GeoFeature { Properties = new GeoFeatureProperties { OrganizationId = resourceOrgId } };
        provider.Add(feature);
        using var client = AuthenticatedClient(server);

        var response = await client.GetAsync($"/api/geoassets/features/{feature.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetFeature_SameOrganization_Returns200()
    {
        var orgId = Guid.NewGuid();
        var (server, provider) = await BuildServerAsync(userOrganizationId: orgId);
        var feature = new GeoFeature { Properties = new GeoFeatureProperties { OrganizationId = orgId } };
        provider.Add(feature);
        using var client = AuthenticatedClient(server);

        var response = await client.GetAsync($"/api/geoassets/features/{feature.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetFeature_UnownedResource_Returns200()
    {
        // Pre-XD01-20 features default to Guid.Empty and must stay reachable.
        var (server, provider) = await BuildServerAsync(userOrganizationId: Guid.NewGuid());
        var feature = new GeoFeature();
        provider.Add(feature);
        using var client = AuthenticatedClient(server);

        var response = await client.GetAsync($"/api/geoassets/features/{feature.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetFeature_DifferentOrganizationWithMatchingGrant_Returns200()
    {
        var granteeOrgId = Guid.NewGuid();
        var resourceOrgId = Guid.NewGuid();
        var grant = new OrganizationGrant
        {
            GranteeOrganizationId  = granteeOrgId,
            ResourceOrganizationId = resourceOrgId,
            AllowedActions         = ["features:read"],
            GrantedBy              = "admin@example.com",
            GrantedAt              = DateTime.UtcNow,
            IsActive               = true,
        };
        var (server, provider) = await BuildServerAsync(userOrganizationId: granteeOrgId, grants: [grant]);
        var feature = new GeoFeature { Properties = new GeoFeatureProperties { OrganizationId = resourceOrgId } };
        provider.Add(feature);
        using var client = AuthenticatedClient(server);

        var response = await client.GetAsync($"/api/geoassets/features/{feature.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── PUT /features/{id} ────────────────────────────────────────────────────

    [Fact]
    public async Task PutFeature_DifferentOrganizationNoGrant_Returns403()
    {
        var (server, provider) = await BuildServerAsync(userOrganizationId: Guid.NewGuid());
        var feature = new GeoFeature { Properties = new GeoFeatureProperties { OrganizationId = Guid.NewGuid() } };
        provider.Add(feature);
        using var client = AuthenticatedClient(server);

        var response = await client.PutAsJsonAsync($"/api/geoassets/features/{feature.Id}", feature);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── DELETE /features/{id} ─────────────────────────────────────────────────

    [Fact]
    public async Task DeleteFeature_DifferentOrganizationNoGrant_Returns403()
    {
        var (server, provider) = await BuildServerAsync(userOrganizationId: Guid.NewGuid());
        var feature = new GeoFeature { Properties = new GeoFeatureProperties { OrganizationId = Guid.NewGuid() } };
        provider.Add(feature);
        using var client = AuthenticatedClient(server);

        var response = await client.DeleteAsync($"/api/geoassets/features/{feature.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DeleteFeature_NonExistentId_Returns204NotForbidden()
    {
        // Preserves the endpoint's pre-existing "delete is a no-op for a missing id" contract —
        // there's no resource to check ownership of, so the resource-aware gate must not block it.
        var (server, _) = await BuildServerAsync(userOrganizationId: Guid.NewGuid());
        using var client = AuthenticatedClient(server);

        var response = await client.DeleteAsync("/api/geoassets/features/does-not-exist");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // ── DELETE /asset-types/{id} ──────────────────────────────────────────────

    [Fact]
    public async Task DeleteAssetType_DifferentOrganizationNoGrant_Returns403()
    {
        var (server, provider) = await BuildServerAsync(userOrganizationId: Guid.NewGuid());
        var assetType = new AssetType { OrganizationId = Guid.NewGuid() };
        provider.AddAssetType(assetType);
        using var client = AuthenticatedClient(server);

        var response = await client.DeleteAsync($"/api/geoassets/asset-types/{assetType.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DeleteAssetType_SameOrganization_Returns204()
    {
        var orgId = Guid.NewGuid();
        var (server, provider) = await BuildServerAsync(userOrganizationId: orgId);
        var assetType = new AssetType { OrganizationId = orgId };
        provider.AddAssetType(assetType);
        using var client = AuthenticatedClient(server);

        var response = await client.DeleteAsync($"/api/geoassets/asset-types/{assetType.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
