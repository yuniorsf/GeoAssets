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

    // ── Asset type roles (XD01-367) ──────────────────────────────────────────

    [Theory]
    [InlineData]
    [InlineData("occupiable-support")]
    [InlineData("occupiable-support", "isp:pole")]
    public async Task PostAssetType_WithRoles_ThenGet_RoundTripsRoles(params string[] roles)
    {
        var provider = new TestAssetProvider();
        using var server = await BuildServerAsync(provider);
        using var client = server.CreateClient();
        var assetType = new AssetType { Name = "Pole", Roles = roles };

        var post = await client.PostAsJsonAsync(new Uri("/api/geoassets/asset-types", UriKind.Relative), assetType, GeoJsonSerializer.Options);
        var raw = await client.GetStringAsync(new Uri("/api/geoassets/asset-types", UriKind.Relative));
        var listed = await client.GetFromJsonAsync<AssetType[]>(new Uri("/api/geoassets/asset-types", UriKind.Relative), GeoJsonSerializer.Options);

        post.StatusCode.Should().Be(System.Net.HttpStatusCode.Created);
        raw.Should().Contain("\"roles\"");
        listed!.Single(t => t.Id == assetType.Id).Roles.Should().Equal(roles);
        provider.GetAssetTypes().Single(t => t.Id == assetType.Id).Roles.Should().Equal(roles);
    }

    [Theory]
    [InlineData("""{"name":"Pole","roles":["Pole"]}""", "MalformedRole")]
    [InlineData("""{"name":"Pole","roles":["a--b"]}""", "MalformedRole")]
    [InlineData("""{"name":"Pole","roles":["a:b:c"]}""", "MalformedRole")]
    [InlineData("""{"name":"Pole","roles":["dup","dup"]}""", "DuplicateRole")]
    [InlineData("""{"name":"Pole","roles":["r0","r1","r2","r3","r4","r5","r6","r7","r8","r9","r10","r11","r12","r13","r14","r15","r16"]}""", "TooManyRoles")]
    public async Task PostAssetType_InvalidRoles_Returns400(string body, string defect)
    {
        var provider = new TestAssetProvider();
        var before = provider.GetAssetTypes().Count;
        using var server = await BuildServerAsync(provider);
        using var client = server.CreateClient();
        using var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");

        var response = await client.PostAsync(new Uri("/api/geoassets/asset-types", UriKind.Relative), content);

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain($"Invalid asset type roles: {defect}.");
        provider.GetAssetTypes().Should().HaveCount(before);
    }

    // ── Layers / layer rules (XD01-153) ──────────────────────────────────────

    private static readonly Uri LayersUri = new("/api/geoassets/layers", UriKind.Relative);
    private static readonly Uri LayerRulesUri = new("/api/geoassets/layer-rules", UriKind.Relative);

    private static StringContent JsonNullBody() => new("null", System.Text.Encoding.UTF8, "application/json");

    [Fact]
    public async Task GetLayers_ReturnsEveryLayerTheProviderHolds()
    {
        var provider = new TestAssetProvider();
        var layer = new Layer { Name = "Poles", GeometryType = GeometryType.Point, Color = "#ff0000", Radius = 5 };
        provider.AddLayer(layer);

        using var server = await BuildServerAsync(provider);
        using var client = server.CreateClient();

        var layers = await client.GetFromJsonAsync<Layer[]>(LayersUri, GeoJsonSerializer.Options);

        layers.Should().ContainSingle().Which.Should().BeEquivalentTo(layer);
    }

    [Fact]
    public async Task PostLayer_AddsItToTheProvider_AndReturns201WithLocation()
    {
        var provider = new TestAssetProvider();
        var layer = new Layer { Name = "Cables", GeometryType = GeometryType.LineString, DashArray = "5, 5" };

        using var server = await BuildServerAsync(provider);
        using var client = server.CreateClient();

        var response = await client.PostAsJsonAsync(LayersUri, layer, GeoJsonSerializer.Options);

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Created);
        response.Headers.Location.Should().Be(new Uri($"/api/geoassets/layers/{layer.Id}", UriKind.Relative));
        provider.GetLayers().Should().ContainSingle().Which.Should().BeEquivalentTo(layer);
    }

    [Fact]
    public async Task PostLayer_NullBody_Returns400_AndAddsNothing()
    {
        var provider = new TestAssetProvider();

        using var server = await BuildServerAsync(provider);
        using var client = server.CreateClient();
        using var body = JsonNullBody();

        var response = await client.PostAsync(LayersUri, body);

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
        provider.GetLayers().Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteLayer_RemovesOnlyThatLayer_AndReturns204()
    {
        var provider = new TestAssetProvider();
        var doomed = new Layer { Name = "Doomed" };
        var kept = new Layer { Name = "Kept" };
        provider.AddLayer(doomed);
        provider.AddLayer(kept);

        using var server = await BuildServerAsync(provider);
        using var client = server.CreateClient();

        var response = await client.DeleteAsync(new Uri($"/api/geoassets/layers/{doomed.Id}", UriKind.Relative));

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.NoContent);
        provider.GetLayers().Should().ContainSingle().Which.Id.Should().Be(kept.Id);
    }

    [Fact]
    public async Task DeleteLayer_UnknownId_StillReturns204()
    {
        using var server = await BuildServerAsync(new TestAssetProvider());
        using var client = server.CreateClient();

        var response = await client.DeleteAsync(new Uri($"/api/geoassets/layers/{Guid.NewGuid()}", UriKind.Relative));

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task GetLayerRules_NoFilter_ReturnsRulesAcrossEveryAssetType()
    {
        var provider = new TestAssetProvider();
        var pointRule = new LayerRule { AssetTypeId = AssetType.Point.Id, LayerId = Guid.NewGuid(), Priority = 1 };
        var lineRule = new LayerRule { AssetTypeId = AssetType.Line.Id, LayerId = Guid.NewGuid(), Priority = 2 };
        provider.AddLayerRule(pointRule);
        provider.AddLayerRule(lineRule);

        using var server = await BuildServerAsync(provider);
        using var client = server.CreateClient();

        var rules = await client.GetFromJsonAsync<LayerRule[]>(LayerRulesUri, GeoJsonSerializer.Options);

        rules.Should().BeEquivalentTo([pointRule, lineRule]);
    }

    [Fact]
    public async Task GetLayerRules_AssetTypeIdFilter_ReturnsOnlyThatAssetTypesRules()
    {
        var provider = new TestAssetProvider();
        var pointRule = new LayerRule { AssetTypeId = AssetType.Point.Id, LayerId = Guid.NewGuid() };
        provider.AddLayerRule(pointRule);
        provider.AddLayerRule(new LayerRule { AssetTypeId = AssetType.Line.Id, LayerId = Guid.NewGuid() });

        using var server = await BuildServerAsync(provider);
        using var client = server.CreateClient();

        var rules = await client.GetFromJsonAsync<LayerRule[]>(
            new Uri($"/api/geoassets/layer-rules?assetTypeId={AssetType.Point.Id}", UriKind.Relative), GeoJsonSerializer.Options);

        rules.Should().ContainSingle().Which.Id.Should().Be(pointRule.Id);
    }

    [Fact]
    public async Task PostLayerRule_ThenGet_RoundTripsConditionsIntact()
    {
        var provider = new TestAssetProvider();
        var rule = new LayerRule
        {
            AssetTypeId = AssetType.Point.Id,
            LayerId = Guid.NewGuid(),
            Priority = 3,
            Conditions =
            [
                new LayerRuleCondition { Attribute = "voltageKv", Operator = LayerRuleOperator.GreaterThanOrEqual, Value = "13.8" },
                new LayerRuleCondition { Attribute = "status", Operator = LayerRuleOperator.NotEquals, Value = "retired" }
            ]
        };

        using var server = await BuildServerAsync(provider);
        using var client = server.CreateClient();

        var response = await client.PostAsJsonAsync(LayerRulesUri, rule, GeoJsonSerializer.Options);
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Created);
        response.Headers.Location.Should().Be(new Uri($"/api/geoassets/layer-rules/{rule.Id}", UriKind.Relative));

        var rules = await client.GetFromJsonAsync<LayerRule[]>(LayerRulesUri, GeoJsonSerializer.Options);

        rules.Should().ContainSingle().Which.Should().BeEquivalentTo(rule);
        rules![0].Conditions.Should().HaveCount(2);
    }

    [Fact]
    public async Task PostLayerRule_NullBody_Returns400_AndAddsNothing()
    {
        var provider = new TestAssetProvider();

        using var server = await BuildServerAsync(provider);
        using var client = server.CreateClient();
        using var body = JsonNullBody();

        var response = await client.PostAsync(LayerRulesUri, body);

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
        provider.GetLayerRules(AssetType.Point.Id).Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteLayerRule_RemovesOnlyThatRule_AndReturns204()
    {
        var provider = new TestAssetProvider();
        var doomed = new LayerRule { AssetTypeId = AssetType.Point.Id, LayerId = Guid.NewGuid() };
        var kept = new LayerRule { AssetTypeId = AssetType.Point.Id, LayerId = Guid.NewGuid() };
        provider.AddLayerRule(doomed);
        provider.AddLayerRule(kept);

        using var server = await BuildServerAsync(provider);
        using var client = server.CreateClient();

        var response = await client.DeleteAsync(new Uri($"/api/geoassets/layer-rules/{doomed.Id}", UriKind.Relative));

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.NoContent);
        provider.GetLayerRules(AssetType.Point.Id).Should().ContainSingle().Which.Id.Should().Be(kept.Id);
    }
}
