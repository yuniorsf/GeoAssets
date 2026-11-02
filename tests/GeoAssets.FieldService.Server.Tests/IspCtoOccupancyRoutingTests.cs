using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Isp;
using GeoAssets.Core.Messaging.Events;
using GeoAssets.Core.Models;
using GeoAssets.FieldService.Server;
using GeoAssets.Identity.Authentication;
using GeoAssets.Infrastructure.Observability;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GeoAssets.FieldService.Server.Tests;

/// <summary>
/// Integration coverage for <c>IspCtoOccupancyRestApiExtensions.MapIspCtoOccupancyApi</c> (XD01-281):
/// adjust/heatmap/reconcile routing, status-code mapping, and the "every endpoint requires an
/// authenticated caller" boundary, mirroring <see cref="IspModuleRoutingTests"/>'s host bootstrap.
/// </summary>
public sealed class IspCtoOccupancyRoutingTests
{
    private static readonly Guid FixedOrganizationId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private sealed class NoOpAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
            => Task.FromResult(AuthenticateResult.NoResult());
    }

    private sealed class FakeGeoCoreClient(
        GeoFeature? feature = null,
        PagedResult<GeoFeature>? queryResult = null) : IGeoCoreClient
    {
        public Task<GeoFeature?> GetFeatureByIdAsync(string id, CancellationToken ct = default) =>
            Task.FromResult(feature);

        public Task<PagedResult<GeoFeature>> QueryFeaturesAsync(AssetQuery query, CancellationToken ct = default) =>
            Task.FromResult(queryResult ?? new PagedResult<GeoFeature> { Items = [], TotalCount = 0 });

        public Task<AssetBatchAppliedEvent> ExecuteAssetDeltaBatchAsync(AssetMutationBatchRequestedEvent batch, CancellationToken ct = default) =>
            Task.FromResult(new AssetBatchAppliedEvent(
                batch.BatchId,
                batch.CatalogInstanceId,
                batch.OrganizationId,
                batch.RequestedByService,
                batch.PerformedByUserId,
                batch.SourceOrderId,
                batch.SourceOrderTypeId,
                batch.ScopeId,
                [],
                null,
                DateTimeOffset.UnixEpoch));

        public Task<IReadOnlyList<string>> GetNeighborIdsAsync(string featureId, string? edgeKind = null, string? targetAssetTypeId = null, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> GetNeighborIdsBatchAsync(IReadOnlyList<string> featureIds, string? edgeKind = null, string? targetAssetTypeId = null, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeTenantContext(Guid organizationId) : ITenantContext
    {
        public Guid CurrentOrganizationId => organizationId;
    }

    private sealed class FakeCurrentUserAccessor(CurrentUser? user) : ICurrentUserAccessor
    {
        public CurrentUser? GetCurrentUser() => user;
    }

    private static GeoFeature CtoFeature(string id, int occupied, int total) =>
        new()
        {
            Id = id,
            Geometry = new GeoAssets.Core.Models.Geometry.GeoPoint(-46.633309, -23.55052),
            Properties = new GeoFeatureProperties
            {
                AssetTypeId = IspAssetTypeIds.Cto.ToString(),
                CustomAttributes = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [CtoCapacityCalculator.OccupiedSplitterPortsKey] = occupied.ToString(CultureInfo.InvariantCulture),
                    [CtoCapacityCalculator.TotalSplitterPortsKey] = total.ToString(CultureInfo.InvariantCulture),
                },
            },
        };

    private static async Task<TestServer> BuildServerAsync(
        IGeoCoreClient? geoCoreClient = null,
        ITenantContext? tenantContext = null,
        ICurrentUserAccessor? currentUserAccessor = null)
    {
        var host = await new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddSingleton(geoCoreClient ?? new FakeGeoCoreClient());
                    services.AddSingleton(tenantContext ?? new FakeTenantContext(FixedOrganizationId));
                    services.AddSingleton<ICurrentUserAccessor>(currentUserAccessor
                        ?? new FakeCurrentUserAccessor(new CurrentUser("test-user-oid", "test@example.com", "Test User", [])));
                    services.AddSingleton<TimeProvider>(new FakeTimeProvider());
                    services.AddSingleton(new GeoAssetsMeter("test"));
                    services.AddAuthentication("Test")
                        .AddScheme<AuthenticationSchemeOptions, NoOpAuthenticationHandler>("Test", _ => { });
                    services.AddAuthorization(options =>
                        options.FallbackPolicy = new AuthorizationPolicyBuilder()
                            .RequireAuthenticatedUser()
                            .Build());
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
                    app.UseEndpoints(endpoints => endpoints.MapIspCtoOccupancyApi());
                });
            })
            .StartAsync();

        return host.GetTestServer();
    }

    [Fact]
    public async Task AdjustOccupancy_WellFormedInstall_Returns200WithUpdatedCounts()
    {
        var feature = CtoFeature("cto-1", occupied: 5, total: 10);
        using var server = await BuildServerAsync(new FakeGeoCoreClient(feature));
        using var client = server.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Authenticated", "true");

        var response = await client.PostAsJsonAsync(
            new Uri("/api/field-service/isp/cto/cto-1/occupancy/adjust", UriKind.Relative),
            new CtoOccupancyAdjustRequest(Delta: 1, CatalogInstanceId: "catalog-1"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<CtoOccupancyAdjustResponse>();
        body!.NewOccupiedCount.Should().Be(6);
        body.TotalPorts.Should().Be(10);
        body.OccupiedPercentage.Should().Be(60);
    }

    [Fact]
    public async Task AdjustOccupancy_RejectedByFactory_Returns400WithFailureReason()
    {
        var feature = CtoFeature("cto-1", occupied: 0, total: 10);
        using var server = await BuildServerAsync(new FakeGeoCoreClient(feature));
        using var client = server.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Authenticated", "true");

        var response = await client.PostAsJsonAsync(
            new Uri("/api/field-service/isp/cto/cto-1/occupancy/adjust", UriKind.Relative),
            new CtoOccupancyAdjustRequest(Delta: -1, CatalogInstanceId: "catalog-1"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("below zero");
    }

    [Fact]
    public async Task AdjustOccupancy_FeatureNotFound_Returns404()
    {
        using var server = await BuildServerAsync(new FakeGeoCoreClient(feature: null));
        using var client = server.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Authenticated", "true");

        var response = await client.PostAsJsonAsync(
            new Uri("/api/field-service/isp/cto/missing-cto/occupancy/adjust", UriKind.Relative),
            new CtoOccupancyAdjustRequest(Delta: 1, CatalogInstanceId: "catalog-1"));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AdjustOccupancy_NoAuthenticatedPrincipal_Returns401()
    {
        var feature = CtoFeature("cto-1", occupied: 5, total: 10);
        using var server = await BuildServerAsync(new FakeGeoCoreClient(feature));
        using var client = server.CreateClient();

        var response = await client.PostAsJsonAsync(
            new Uri("/api/field-service/isp/cto/cto-1/occupancy/adjust", UriKind.Relative),
            new CtoOccupancyAdjustRequest(Delta: 1, CatalogInstanceId: "catalog-1"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetHeatmap_ReturnsOnlyCtosAtOrAboveThreshold()
    {
        var aboveThreshold = CtoFeature("cto-above", occupied: 9, total: 10);
        var belowThreshold = CtoFeature("cto-below", occupied: 1, total: 10);
        var queryResult = new PagedResult<GeoFeature> { Items = [aboveThreshold, belowThreshold], TotalCount = 2 };
        using var server = await BuildServerAsync(new FakeGeoCoreClient(queryResult: queryResult));
        using var client = server.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Authenticated", "true");

        var response = await client.GetAsync(
            new Uri("/api/field-service/isp/cto/occupancy/heatmap?thresholdPercentage=80", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<List<CtoOccupancyHeatmapEntry>>();
        body.Should().ContainSingle();
        body![0].CtoFeatureId.Should().Be("cto-above");
    }

    [Fact]
    public async Task ReconcileOccupancy_MatchingCounts_HasNoDiscrepancy()
    {
        var feature = CtoFeature("cto-1", occupied: 5, total: 10);
        using var server = await BuildServerAsync(new FakeGeoCoreClient(feature));
        using var client = server.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Authenticated", "true");

        var response = await client.PostAsJsonAsync(
            new Uri("/api/field-service/isp/cto/cto-1/occupancy/reconcile", UriKind.Relative),
            new CtoOccupancyReconcileRequest(BssReportedOccupied: 5));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<CtoOccupancyDiscrepancy>();
        body!.HasDiscrepancy.Should().BeFalse();
        body.Difference.Should().Be(0);
    }

    [Fact]
    public async Task ReconcileOccupancy_MismatchedCounts_HasDiscrepancyWithCorrectDifference()
    {
        var feature = CtoFeature("cto-1", occupied: 5, total: 10);
        using var server = await BuildServerAsync(new FakeGeoCoreClient(feature));
        using var client = server.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Authenticated", "true");

        var response = await client.PostAsJsonAsync(
            new Uri("/api/field-service/isp/cto/cto-1/occupancy/reconcile", UriKind.Relative),
            new CtoOccupancyReconcileRequest(BssReportedOccupied: 8));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<CtoOccupancyDiscrepancy>();
        body!.HasDiscrepancy.Should().BeTrue();
        body.Difference.Should().Be(3);
    }

    [Fact]
    public async Task ReconcileOccupancy_FeatureNotFound_Returns404()
    {
        using var server = await BuildServerAsync(new FakeGeoCoreClient(feature: null));
        using var client = server.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Authenticated", "true");

        var response = await client.PostAsJsonAsync(
            new Uri("/api/field-service/isp/cto/missing-cto/occupancy/reconcile", UriKind.Relative),
            new CtoOccupancyReconcileRequest(BssReportedOccupied: 5));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
