using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Messaging.Events;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Services;
using GeoAssets.Infrastructure.Observability;
using GeoAssets.Workflow.FieldWork;
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
using Xunit;

namespace GeoAssets.FieldService.Server.Tests;

/// <summary>
/// Integration coverage for <c>IspOtdrFaultDispatchRestApiExtensions.MapIspOtdrFaultDispatchApi</c>
/// (XD01-283): fault-dispatch routing, status-code mapping, and the "every endpoint requires an
/// authenticated caller" boundary, mirroring <see cref="IspCtoOccupancyRoutingTests"/>'s host
/// bootstrap. The engine and dispatch service are wired for real here (both are pure/stateless) —
/// only <see cref="IGeoCoreClient"/> is faked.
/// </summary>
public sealed class IspOtdrFaultDispatchRoutingTests
{
    private sealed class NoOpAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
            => Task.FromResult(AuthenticateResult.NoResult());
    }

    private sealed class FakeGeoCoreClient(GeoFeature? feature = null) : IGeoCoreClient
    {
        public Task<GeoFeature?> GetFeatureByIdAsync(string id, CancellationToken ct = default) =>
            Task.FromResult(feature);

        public Task<PagedResult<GeoFeature>> QueryFeaturesAsync(AssetQuery query, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<AssetBatchAppliedEvent> ExecuteAssetDeltaBatchAsync(AssetMutationBatchRequestedEvent batch, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<string>> GetNeighborIdsAsync(string featureId, string? edgeKind = null, string? targetAssetTypeId = null, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> GetNeighborIdsBatchAsync(IReadOnlyList<string> featureIds, string? edgeKind = null, string? targetAssetTypeId = null, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private static GeoFeature CableFeature(string id = "cable-1") =>
        new()
        {
            Id = id,
            Geometry = new GeoLineString([(-46.633, -23.55), (-46.400, -23.55)]),
        };

    private static IspOtdrFaultDispatchRequest DirectDistanceRequest(string cableFeatureId = "cable-1") =>
        new(
            CableFeatureId: cableFeatureId,
            SorTraceBytes: null,
            DirectOpticalDistanceMeters: 500.0,
            HelixFactor: 1.0,
            CentralOfficeSlackMeters: 0.0,
            SlackPoints: [],
            PolesAlongRoute: []);

    private static async Task<TestServer> BuildServerAsync(IGeoCoreClient? geoCoreClient = null)
    {
        var host = await new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddSingleton(geoCoreClient ?? new FakeGeoCoreClient());
                    services.AddSingleton<IOtdrFaultCorrelationEngine, OtdrFaultCorrelationEngine>();
                    services.AddSingleton<OtdrEmergencyFaultDispatchService>();
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
                    app.UseEndpoints(endpoints => endpoints.MapIspOtdrFaultDispatchApi());
                });
            })
            .StartAsync();

        return host.GetTestServer();
    }

    [Fact]
    public async Task FaultDispatch_WellFormedDirectDistanceRequestAgainstRealCableGeometry_Returns200WithSensibleResult()
    {
        using var server = await BuildServerAsync(new FakeGeoCoreClient(CableFeature()));
        using var client = server.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Authenticated", "true");

        var response = await client.PostAsJsonAsync(
            new Uri("/api/field-service/isp/otdr/fault-dispatch", UriKind.Relative),
            DirectDistanceRequest());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<OtdrFaultCorrelationResult>();
        body.Should().NotBeNull();
        body!.RouteDistanceMeters.Should().Be(500.0);
        body.Coordinate.Should().NotBeNull();
    }

    [Fact]
    public async Task FaultDispatch_CableFeatureNotFound_Returns404()
    {
        using var server = await BuildServerAsync(new FakeGeoCoreClient(feature: null));
        using var client = server.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Authenticated", "true");

        var response = await client.PostAsJsonAsync(
            new Uri("/api/field-service/isp/otdr/fault-dispatch", UriKind.Relative),
            DirectDistanceRequest(cableFeatureId: "missing-cable"));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task FaultDispatch_NeitherOpticalDistanceSourceProvided_Returns400WithFailureReasonInBody()
    {
        using var server = await BuildServerAsync(new FakeGeoCoreClient(CableFeature()));
        using var client = server.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Authenticated", "true");

        var request = new IspOtdrFaultDispatchRequest(
            CableFeatureId: "cable-1",
            SorTraceBytes: null,
            DirectOpticalDistanceMeters: null,
            HelixFactor: 1.0,
            CentralOfficeSlackMeters: 0.0,
            SlackPoints: [],
            PolesAlongRoute: []);

        var response = await client.PostAsJsonAsync(
            new Uri("/api/field-service/isp/otdr/fault-dispatch", UriKind.Relative),
            request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task FaultDispatch_NoAuthenticatedPrincipal_Returns401()
    {
        using var server = await BuildServerAsync(new FakeGeoCoreClient(CableFeature()));
        using var client = server.CreateClient();

        var response = await client.PostAsJsonAsync(
            new Uri("/api/field-service/isp/otdr/fault-dispatch", UriKind.Relative),
            DirectDistanceRequest());

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
