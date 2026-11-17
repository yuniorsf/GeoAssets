using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Services;
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
        public Task<IReadOnlyList<OrganizationGrantSyncEntry>> GetSyncBatchAsync(long afterSequence, int take, CancellationToken ct = default) => throw new NotSupportedException();
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

        var json = await client.GetStringAsync(new Uri("/api/geoassets/features/bounds?minLon=0&minLat=0&maxLon=1&maxLat=1", UriKind.Relative));
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

        var json = await client.GetStringAsync(new Uri("/api/geoassets/features/bounds?minLon=0&minLat=0&maxLon=10&maxLat=10", UriKind.Relative));
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetArrayLength().Should().Be(10_000);
    }

    [Fact]
    public async Task QueryFeatures_WithAssetTypeAndAttributeFilters_ReturnsMatchingFeaturesOnly()
    {
        var provider = new TestAssetProvider();
        provider.Add(new GeoFeature
        {
            Id = "lamp-sp-1",
            Properties = new GeoFeatureProperties
            {
                AssetTypeId = "type-lamp",
                CustomAttributes = new Dictionary<string, string>(StringComparer.Ordinal) { ["scopeId"] = "3550308", ["wattageW"] = "150" }
            }
        });
        provider.Add(new GeoFeature
        {
            Id = "lamp-rj-1",
            Properties = new GeoFeatureProperties
            {
                AssetTypeId = "type-lamp",
                CustomAttributes = new Dictionary<string, string>(StringComparer.Ordinal) { ["scopeId"] = "3304557", ["wattageW"] = "150" }
            }
        });

        using var server = await BuildServerAsync(provider);
        using var client = server.CreateClient();

        var query = new AssetQuery
        {
            AssetTypeId      = "type-lamp",
            AttributeFilters = new Dictionary<string, string>(StringComparer.Ordinal) { ["scopeId"] = "3550308" }
        };
        var response = await client.PostAsJsonAsync(new Uri("/api/geoassets/features/query", UriKind.Relative), query, GeoJsonSerializer.Options);
        response.EnsureSuccessStatusCode();

        var page = await response.Content.ReadFromJsonAsync<PagedResult<GeoFeature>>(GeoJsonSerializer.Options);
        page.Should().NotBeNull();
        page!.TotalCount.Should().Be(1);
        page.Items.Should().ContainSingle().Which.Id.Should().Be("lamp-sp-1");
    }

    [Fact]
    public async Task TopologyNeighborsBatch_FiltersByEdgeKindAndTargetAssetTypeId()
    {
        var provider = new TestAssetProvider();
        provider.Add(new GeoFeature
        {
            Id = "tx-1",
            Topology =
            [
                new TopoEdge { TargetId = "pole-1", Kind = "feeds" },
                new TopoEdge { TargetId = "pole-2", Kind = "adjacent" },
                new TopoEdge { TargetId = "sub-1", Kind = "feeds" }
            ]
        });
        provider.Add(new GeoFeature { Id = "pole-1", Properties = new GeoFeatureProperties { AssetTypeId = "type-pole" } });
        provider.Add(new GeoFeature { Id = "pole-2", Properties = new GeoFeatureProperties { AssetTypeId = "type-pole" } });
        provider.Add(new GeoFeature { Id = "sub-1", Properties = new GeoFeatureProperties { AssetTypeId = "type-sub" } });

        using var server = await BuildServerAsync(provider);
        using var client = server.CreateClient();

        var batchReq = new TopologyNeighborsBatchRequest
        {
            FeatureIds        = ["tx-1"],
            Kind              = "feeds",
            TargetAssetTypeId = "type-pole"
        };
        var response = await client.PostAsJsonAsync(new Uri("/api/geoassets/features/topology/neighbors-batch", UriKind.Relative), batchReq, GeoJsonSerializer.Options);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<Dictionary<string, List<string>>>(GeoJsonSerializer.Options);
        result.Should().NotBeNull();
        result!["tx-1"].Should().Equal("pole-1");
    }

    [Fact]
    public async Task GisExport_ReturnsGeoJsonFeatureCollectionWithVersionedPoleFields()
    {
        var provider = new TestAssetProvider();
        provider.Add(new GeoFeature
        {
            Id = "pole-1",
            Geometry = new GeoPoint(-46.6, -23.5),
            Properties = new GeoFeatureProperties { AssetTypeId = "type-pole", Name = "Poste 1" }
        });
        provider.Add(new GeoFeature
        {
            Id = "lamp-1",
            Geometry = new GeoPoint(-46.7, -23.6),
            Properties = new GeoFeatureProperties { AssetTypeId = "type-lamp", Name = "Lamp 1" }
        });

        using var server = await BuildServerAsync(provider);
        using var client = server.CreateClient();

        var response = await client.GetAsync(new Uri("/api/geoassets/export/gis/v1?assetTypeId=type-pole", UriKind.Relative));
        response.EnsureSuccessStatusCode();
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/geo+json");

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("type").GetString().Should().Be("FeatureCollection");
        var features = doc.RootElement.GetProperty("features");
        features.GetArrayLength().Should().Be(1);

        var props = features[0].GetProperty("properties");
        props.GetProperty("poleFeatureId").GetString().Should().Be("pole-1");
        props.GetProperty("schemaVersion").GetString().Should().Be("1");
        props.GetProperty("complianceStatus").GetString().Should().Be("unknown");
    }

    // ── Asset type zoom range (XD01-311) ─────────────────────────────────────

    [Theory]
    [InlineData(13, 15)]
    [InlineData(15, null)]
    [InlineData(null, 13)]
    [InlineData(null, null)]
    public async Task PostAssetType_ThenGetAssetTypes_RoundTripsTheZoomRange(int? min, int? max)
    {
        var provider = new TestAssetProvider();
        using var server = await BuildServerAsync(provider);
        using var client = server.CreateClient();
        var assetType = new AssetType { Name = "Pole", MinZoom = min, MaxZoom = max };

        var post = await client.PostAsJsonAsync(new Uri("/api/geoassets/asset-types", UriKind.Relative), assetType, GeoJsonSerializer.Options);
        var listed = await client.GetFromJsonAsync<AssetType[]>(new Uri("/api/geoassets/asset-types", UriKind.Relative), GeoJsonSerializer.Options);

        post.StatusCode.Should().Be(System.Net.HttpStatusCode.Created);
        var returned = listed!.Single(t => t.Id == assetType.Id);
        returned.MinZoom.Should().Be(min);
        returned.MaxZoom.Should().Be(max);
    }

    [Theory]
    [InlineData("""{"name":"Pole","minZoom":-1}""")]
    [InlineData("""{"name":"Pole","maxZoom":25}""")]
    [InlineData("""{"name":"Pole","minZoom":16,"maxZoom":15}""")]
    public async Task PostAssetType_InvalidZoomRange_Returns400AndStoresNothing(string body)
    {
        var provider = new TestAssetProvider();
        var before = provider.GetAssetTypes().Count;
        using var server = await BuildServerAsync(provider);
        using var client = server.CreateClient();
        using var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");

        var response = await client.PostAsync(new Uri("/api/geoassets/asset-types", UriKind.Relative), content);

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
        provider.GetAssetTypes().Should().HaveCount(before);
    }
}
