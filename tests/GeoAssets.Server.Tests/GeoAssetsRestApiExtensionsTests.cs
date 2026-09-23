using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;
using GeoAssets.Identity.Authorization.Services;
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
/// Proves <c>GET /features/bounds</c> caps its response to <c>MaxBoundsFeatureCount</c>
/// (XD01-170 finding #3) — before this fix, a dense large-bbox query returned every matching
/// feature with no ceiling, observed at ~15.9MB for one query during XD01-159's audit.
/// </summary>
public class GeoAssetsRestApiExtensionsTests
{
    private sealed class FakeAuthorizationService : IGeoAuthorizationService
    {
        public Task<bool> IsInRoleAsync(string roleName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> HasClaimAsync(string claimType, string? claimValue = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> HasPermissionAsync(string permissionCode, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> EvaluatePolicyAsync(string policyName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> EvaluatePolicyAsync(AppPolicy policy, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AuthorizationContext> GetAuthorizationContextAsync(CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class NeverCalledDbContextFactory : IDbContextFactory<GeoAssetsDbContext>
    {
        public GeoAssetsDbContext CreateDbContext() => throw new NotSupportedException();
        public Task<GeoAssetsDbContext> CreateDbContextAsync(CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class NeverCalledOrganizationGrantRepository : IOrganizationGrantRepository
    {
        public Task<OrganizationGrant?> GetByIdAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<OrganizationGrant>> GetAllAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<OrganizationGrant>> GetActiveGrantsAsync(Guid granteeOrganizationId, Guid resourceOrganizationId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<OrganizationGrant>> GetActiveGrantsForGranteeAsync(Guid granteeOrganizationId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task AddAsync(OrganizationGrant grant, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpdateAsync(OrganizationGrant grant, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveChangesAsync(CancellationToken ct = default) => throw new NotSupportedException();
    }

    private static GeoFeature PointFeature(double lon, double lat) => new()
    {
        Geometry = new GeoPoint(lon, lat),
        Properties = { AssetTypeId = AssetType.Point.Id.ToString() }
    };

    private static async Task<TestServer> BuildServerAsync(IAssetProvider provider)
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
                    services.AddSingleton<IGeoAuthorizationService>(new FakeAuthorizationService());
                    services.AddSingleton<IOrganizationGrantRepository, NeverCalledOrganizationGrantRepository>();
                    services.AddSingleton(provider);
                    services.AddSingleton<IDbContextFactory<GeoAssetsDbContext>, NeverCalledDbContextFactory>();
                    services.AddSingleton<WmsPostGisRenderer>();
                });
                webHost.Configure(app =>
                {
                    app.Use(async (ctx, next) =>
                    {
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

        return host.GetTestServer();
    }

    [Fact]
    public async Task GetBounds_FewerFeaturesThanCap_ReturnsAllOfThem()
    {
        var provider = new TestAssetProvider();
        for (var i = 0; i < 5; i++) provider.Add(PointFeature(i * 0.1, i * 0.1));

        using var server = await BuildServerAsync(provider);
        using var client = server.CreateClient();

        var json = await client.GetStringAsync(
            "/api/geoassets/features/bounds?minLon=0&minLat=0&maxLon=1&maxLat=1");
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetArrayLength().Should().Be(5);
    }

    [Fact]
    public async Task GetBounds_MoreFeaturesThanCap_TruncatesToCap()
    {
        // One over the 10,000 cap — proves the endpoint actually enforces a ceiling rather than
        // returning everything the provider hands back (this test fails without the fix: the
        // pre-fix endpoint returned all 10,001 features unbounded).
        const int seedCount = 10_001;
        var provider = new TestAssetProvider();
        for (var i = 0; i < seedCount; i++)
            provider.Add(PointFeature(i % 1000 * 0.001, i / 1000 * 0.001));

        using var server = await BuildServerAsync(provider);
        using var client = server.CreateClient();

        var json = await client.GetStringAsync(
            "/api/geoassets/features/bounds?minLon=0&minLat=0&maxLon=10&maxLat=10");
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetArrayLength().Should().Be(10_000);
    }
}
