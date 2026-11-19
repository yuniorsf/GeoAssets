using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Providers;
using GeoAssets.Core.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace GeoAssets.Server.Tests;

/// <summary>
/// XD01-386: <c>GET /api/geoassets/organizations/{organizationId}/features/{id}[/descendants]</c> —
/// service-caller-only reads of the catalog as one organization sees it. Real
/// <see cref="FilteringOrganizationCatalogFactory"/> over an in-memory catalog, so cross-organization
/// invisibility is the production filtering, not a stub.
/// </summary>
public sealed class OrganizationScopedFeaturesEndpointTests
{
    private const string RolesHeader = "X-Test-Roles";
    private const string EdgeKind = "feeds";
    private static readonly Guid OrgA = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a");
    private static readonly Guid OrgB = Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000b");

    private sealed class HeaderAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(RolesHeader, out var roles)) return Task.FromResult(AuthenticateResult.NoResult());

            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, "caller") };
            claims.AddRange(roles.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries).Select(r => new Claim(ClaimTypes.Role, r)));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(
                new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")), "Test")));
        }
    }

    private sealed class Harness : IAsyncDisposable
    {
        private IHost? _host;

        public TestAssetProvider Catalog { get; } = new();

        public HttpClient Client { get; private set; } = null!;

        public async Task StartAsync()
        {
            _host = await new HostBuilder()
                .ConfigureWebHost(webHost =>
                {
                    webHost.UseTestServer();
                    webHost.ConfigureServices(services =>
                    {
                        services.AddRouting();
                        services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, HeaderAuthenticationHandler>("Test", _ => { });
                        services.AddAuthorization();
                        services.AddSingleton<IOrganizationCatalogFactory>(new FilteringOrganizationCatalogFactory(Catalog));
                    });
                    webHost.Configure(app =>
                    {
                        app.UseRouting();
                        app.UseAuthentication();
                        app.UseAuthorization();
                        app.UseEndpoints(endpoints => endpoints.MapOrganizationScopedFeaturesApi());
                    });
                })
                .StartAsync();
            Client = _host.GetTestServer().CreateClient();
        }

        public async Task<HttpResponseMessage> GetAsync(string path, string? roles = GeoAuthorizationHandler.ServiceCallerAppRole)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
            if (roles is not null) request.Headers.Add(RolesHeader, roles);
            return await Client.SendAsync(request);
        }

        public async ValueTask DisposeAsync()
        {
            Client?.Dispose();
            if (_host is not null)
            {
                await _host.StopAsync();
                _host.Dispose();
            }
        }
    }

    private static GeoFeature Feature(string id, Guid organizationId, params string[] downstream) => new()
    {
        Id = id,
        Geometry = new GeoPoint(-43.2, -22.9),
        Properties = new GeoFeatureProperties { Name = id, AssetTypeId = "pole", OrganizationId = organizationId },
        Topology = [.. downstream.Select(t => new TopoEdge { TargetId = t, Kind = EdgeKind })],
    };

    private static string FeatureUrl(Guid organizationId, string id) =>
        $"/api/geoassets/organizations/{organizationId:D}/features/{id}";

    private static string DescendantsUrl(Guid organizationId, string id, string query = "") =>
        $"{FeatureUrl(organizationId, id)}/descendants?edgeKind={EdgeKind}{query}";

    private static async Task<PagedResult<GeoFeature>> ReadPageAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<PagedResult<GeoFeature>>(GeoJsonSerializer.Options))!;

    // ── Authentication / authorization ─────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("/descendants?edgeKind=feeds")]
    public async Task Get_Anonymous_Returns401(string suffix)
    {
        await using var harness = new Harness();
        await harness.StartAsync();
        harness.Catalog.Add(Feature("f1", OrgA));

        using var response = await harness.GetAsync(FeatureUrl(OrgA, "f1") + suffix, roles: null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("Administrator", "")]
    [InlineData("Supervisor,Administrator", "")]
    [InlineData("SomeOtherRole", "")]
    [InlineData("Administrator", "/descendants?edgeKind=feeds")]
    [InlineData("SomeOtherRole", "/descendants?edgeKind=feeds")]
    public async Task Get_AuthenticatedWithoutTheServiceCallerAppRole_Returns403(string roles, string suffix)
    {
        await using var harness = new Harness();
        await harness.StartAsync();
        harness.Catalog.Add(Feature("f1", OrgA));

        using var response = await harness.GetAsync(FeatureUrl(OrgA, "f1") + suffix, roles);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── GET features/{id} ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetFeature_OwnOrganizationFeature_ReturnsIt()
    {
        await using var harness = new Harness();
        await harness.StartAsync();
        harness.Catalog.Add(Feature("a-pole", OrgA));

        using var response = await harness.GetAsync(FeatureUrl(OrgA, "a-pole"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var feature = await response.Content.ReadFromJsonAsync<GeoFeature>(GeoJsonSerializer.Options);
        feature!.Id.Should().Be("a-pole");
        feature.Properties.OrganizationId.Should().Be(OrgA);
    }

    [Fact]
    public async Task GetFeature_AnotherOrganizationsFeature_Returns404()
    {
        await using var harness = new Harness();
        await harness.StartAsync();
        harness.Catalog.Add(Feature("a-pole", OrgA));

        using var response = await harness.GetAsync(FeatureUrl(OrgB, "a-pole"));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, "cross-organization reads look like a missing feature, never a 403");
    }

    [Fact]
    public async Task GetFeature_SharedFeature_IsVisibleToAnyOrganization()
    {
        await using var harness = new Harness();
        await harness.StartAsync();
        harness.Catalog.Add(Feature("shared-switch", Guid.Empty));

        using var responseA = await harness.GetAsync(FeatureUrl(OrgA, "shared-switch"));
        using var responseB = await harness.GetAsync(FeatureUrl(OrgB, "shared-switch"));

        responseA.StatusCode.Should().Be(HttpStatusCode.OK);
        responseB.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetFeature_UnknownId_Returns404()
    {
        await using var harness = new Harness();
        await harness.StartAsync();

        using var response = await harness.GetAsync(FeatureUrl(OrgA, "missing"));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetFeature_NonGuidOrganizationSegment_DoesNotMatchTheRoute()
    {
        await using var harness = new Harness();
        await harness.StartAsync();
        harness.Catalog.Add(Feature("f1", OrgA));

        using var response = await harness.GetAsync("/api/geoassets/organizations/not-a-guid/features/f1");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── GET features/{id}/descendants ──────────────────────────────────────────

    [Fact]
    public async Task GetDescendants_ReturnsDownstreamFeaturesInOrdinalIdOrderWithTotalCount()
    {
        await using var harness = new Harness();
        await harness.StartAsync();
        harness.Catalog.Add(Feature("root", OrgA, "c", "a", "b"));
        harness.Catalog.Add(Feature("c", OrgA));
        harness.Catalog.Add(Feature("a", OrgA, "d"));
        harness.Catalog.Add(Feature("b", OrgA));
        harness.Catalog.Add(Feature("d", OrgA));
        harness.Catalog.Add(Feature("unrelated", OrgA));

        using var response = await harness.GetAsync(DescendantsUrl(OrgA, "root"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = await ReadPageAsync(response);
        page.Items.Select(f => f.Id).Should().Equal("a", "b", "c", "d");
        page.TotalCount.Should().Be(4);
    }

    [Fact]
    public async Task GetDescendants_OnlyFollowsTheRequestedEdgeKind()
    {
        await using var harness = new Harness();
        await harness.StartAsync();
        var root = Feature("root", OrgA, "feeds-child");
        root.Topology.Add(new TopoEdge { TargetId = "other-child", Kind = "connected-to" });
        harness.Catalog.Add(root);
        harness.Catalog.Add(Feature("feeds-child", OrgA));
        harness.Catalog.Add(Feature("other-child", OrgA));

        using var response = await harness.GetAsync(DescendantsUrl(OrgA, "root"));

        (await ReadPageAsync(response)).Items.Select(f => f.Id).Should().Equal("feeds-child");
    }

    [Fact]
    public async Task GetDescendants_SkipAndTake_PageStablyThroughTheOrderedSet()
    {
        await using var harness = new Harness();
        await harness.StartAsync();
        var ids = Enumerable.Range(0, 7).Select(i => $"d-{i}").ToArray();
        harness.Catalog.Add(Feature("root", OrgA, [.. ids]));
        foreach (var id in ids) harness.Catalog.Add(Feature(id, OrgA));

        using var first = await harness.GetAsync(DescendantsUrl(OrgA, "root", "&skip=0&take=3"));
        using var second = await harness.GetAsync(DescendantsUrl(OrgA, "root", "&skip=3&take=3"));
        using var last = await harness.GetAsync(DescendantsUrl(OrgA, "root", "&skip=6&take=3"));
        using var beyond = await harness.GetAsync(DescendantsUrl(OrgA, "root", "&skip=50&take=3"));

        var firstPage = await ReadPageAsync(first);
        firstPage.Items.Select(f => f.Id).Should().Equal("d-0", "d-1", "d-2");
        firstPage.TotalCount.Should().Be(7);
        (await ReadPageAsync(second)).Items.Select(f => f.Id).Should().Equal("d-3", "d-4", "d-5");
        (await ReadPageAsync(last)).Items.Select(f => f.Id).Should().Equal("d-6");
        var beyondPage = await ReadPageAsync(beyond);
        beyondPage.Items.Should().BeEmpty();
        beyondPage.TotalCount.Should().Be(7);
    }

    [Fact]
    public async Task GetDescendants_NoTake_DefaultsToFiveHundred()
    {
        await using var harness = new Harness();
        await harness.StartAsync();
        var ids = Enumerable.Range(0, 520).Select(i => $"d-{i:D4}").ToArray();
        harness.Catalog.Add(Feature("root", OrgA, [.. ids]));
        foreach (var id in ids) harness.Catalog.Add(Feature(id, OrgA));

        using var response = await harness.GetAsync(DescendantsUrl(OrgA, "root"));

        var page = await ReadPageAsync(response);
        page.Items.Should().HaveCount(OrganizationScopedFeaturesRestApiExtensions.DefaultDescendantsPageSize);
        page.TotalCount.Should().Be(520);
    }

    [Fact]
    public async Task GetDescendants_TakeAboveTheMaximum_IsClampedToFiveThousand()
    {
        await using var harness = new Harness();
        await harness.StartAsync();
        var ids = Enumerable.Range(0, 5_100).Select(i => $"d-{i:D5}").ToArray();
        harness.Catalog.Add(Feature("root", OrgA, [.. ids]));
        foreach (var id in ids) harness.Catalog.Add(Feature(id, OrgA));

        using var response = await harness.GetAsync(DescendantsUrl(OrgA, "root", "&take=100000"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = await ReadPageAsync(response);
        page.Items.Should().HaveCount(OrganizationScopedFeaturesRestApiExtensions.MaxDescendantsPageSize);
        page.TotalCount.Should().Be(5_100);
    }

    [Theory]
    [InlineData("&skip=-1")]
    [InlineData("&take=0")]
    [InlineData("&take=-5")]
    [InlineData("&skip=-1&take=0")]
    public async Task GetDescendants_NegativeSkipOrNonPositiveTake_Returns400(string query)
    {
        await using var harness = new Harness();
        await harness.StartAsync();
        harness.Catalog.Add(Feature("root", OrgA));

        using var response = await harness.GetAsync(DescendantsUrl(OrgA, "root", query));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetDescendants_UnknownRoot_Returns404()
    {
        await using var harness = new Harness();
        await harness.StartAsync();

        using var response = await harness.GetAsync(DescendantsUrl(OrgA, "missing"));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetDescendants_RootBelongsToAnotherOrganization_Returns404()
    {
        await using var harness = new Harness();
        await harness.StartAsync();
        harness.Catalog.Add(Feature("a-root", OrgA, "a-child"));
        harness.Catalog.Add(Feature("a-child", OrgA));

        using var response = await harness.GetAsync(DescendantsUrl(OrgB, "a-root"));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetDescendants_DescendantsOfAnotherOrganization_AreNotDisclosed()
    {
        await using var harness = new Harness();
        await harness.StartAsync();
        harness.Catalog.Add(Feature("shared-root", Guid.Empty, "a-child", "b-child", "shared-child"));
        harness.Catalog.Add(Feature("a-child", OrgA));
        harness.Catalog.Add(Feature("b-child", OrgB));
        harness.Catalog.Add(Feature("shared-child", Guid.Empty));

        using var response = await harness.GetAsync(DescendantsUrl(OrgA, "shared-root"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadPageAsync(response)).Items.Select(f => f.Id).Should().Equal("a-child", "shared-child");
    }

    [Fact]
    public async Task GetDescendants_SharedRoot_IsVisibleToAnyOrganization()
    {
        await using var harness = new Harness();
        await harness.StartAsync();
        harness.Catalog.Add(Feature("shared-root", Guid.Empty, "shared-child"));
        harness.Catalog.Add(Feature("shared-child", Guid.Empty));

        using var response = await harness.GetAsync(DescendantsUrl(OrgB, "shared-root"));

        (await ReadPageAsync(response)).Items.Select(f => f.Id).Should().Equal("shared-child");
    }
}
