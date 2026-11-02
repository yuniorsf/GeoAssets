using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Messaging.Events;
using GeoAssets.Core.Models;
using GeoAssets.FieldService.Server;
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
/// Integration coverage for <c>IspModuleRestApiExtensions.MapIspModuleApi</c> (XD01-279) — the
/// webhook ingestion shell's envelope validation plus the <see cref="IGeoCoreClient"/> proxy's
/// found/not-found round-trip, and the default "every endpoint requires an authenticated caller"
/// boundary, mirroring <c>EvidenceVerificationApiTests</c>'s host bootstrap exactly.
/// </summary>
public sealed class IspModuleRoutingTests
{
    private sealed class NoOpAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
            => Task.FromResult(AuthenticateResult.NoResult());
    }

    private sealed class FakeGeoCoreClient(GeoFeature? feature) : IGeoCoreClient
    {
        public Task<GeoFeature?> GetFeatureByIdAsync(string id, CancellationToken ct = default) =>
            Task.FromResult(feature);

        public Task<AssetBatchAppliedEvent> ExecuteAssetDeltaBatchAsync(AssetMutationBatchRequestedEvent batch, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<PagedResult<GeoFeature>> QueryFeaturesAsync(AssetQuery query, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<string>> GetNeighborIdsAsync(string featureId, string? edgeKind = null, string? targetAssetTypeId = null, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> GetNeighborIdsBatchAsync(IReadOnlyList<string> featureIds, string? edgeKind = null, string? targetAssetTypeId = null, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private static async Task<TestServer> BuildServerAsync(IGeoCoreClient? geoCoreClient = null)
    {
        var host = await new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddSingleton(geoCoreClient ?? new FakeGeoCoreClient(feature: null));
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
                    app.UseEndpoints(endpoints => endpoints.MapIspModuleApi());
                });
            })
            .StartAsync();

        return host.GetTestServer();
    }

    [Fact]
    public async Task GetFeatureByIdViaGeoCore_NoAuthenticatedPrincipal_Returns401()
    {
        using var server = await BuildServerAsync();
        using var client = server.CreateClient();

        var response = await client.GetAsync(new Uri("/api/field-service/isp/geo-core/features/feature-1", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetFeatureByIdViaGeoCore_FeatureFound_ReturnsOkWithBody()
    {
        var feature = new GeoFeature { Id = "feature-1" };
        using var server = await BuildServerAsync(new FakeGeoCoreClient(feature));
        using var client = server.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Authenticated", "true");

        var response = await client.GetAsync(new Uri("/api/field-service/isp/geo-core/features/feature-1", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<GeoFeature>();
        body!.Id.Should().Be("feature-1");
    }

    [Fact]
    public async Task GetFeatureByIdViaGeoCore_FeatureNotFound_Returns404()
    {
        using var server = await BuildServerAsync(new FakeGeoCoreClient(feature: null));
        using var client = server.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Authenticated", "true");

        var response = await client.GetAsync(new Uri("/api/field-service/isp/geo-core/features/missing-feature", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task IngestWebhook_WellFormedPayload_Returns202WithAcceptedStatus()
    {
        using var server = await BuildServerAsync();
        using var client = server.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Authenticated", "true");

        var response = await client.PostAsJsonAsync(
            new Uri("/api/field-service/isp/webhooks/ingest", UriKind.Relative),
            new IspWebhookIngestRequest("ixc-soft", "order.updated", """{"status":"completed"}"""));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var body = await response.Content.ReadFromJsonAsync<IspWebhookAckResponse>();
        body!.Status.Should().Be("Accepted");
    }

    [Fact]
    public async Task IngestWebhook_MissingProviderId_Returns400()
    {
        using var server = await BuildServerAsync();
        using var client = server.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Authenticated", "true");

        var response = await client.PostAsJsonAsync(
            new Uri("/api/field-service/isp/webhooks/ingest", UriKind.Relative),
            new IspWebhookIngestRequest("", "order.updated", """{"status":"completed"}"""));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task IngestWebhook_MissingEventType_Returns400()
    {
        using var server = await BuildServerAsync();
        using var client = server.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Authenticated", "true");

        var response = await client.PostAsJsonAsync(
            new Uri("/api/field-service/isp/webhooks/ingest", UriKind.Relative),
            new IspWebhookIngestRequest("ixc-soft", "   ", """{"status":"completed"}"""));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task IngestWebhook_MissingPayloadJson_Returns400()
    {
        using var server = await BuildServerAsync();
        using var client = server.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Authenticated", "true");

        var response = await client.PostAsJsonAsync(
            new Uri("/api/field-service/isp/webhooks/ingest", UriKind.Relative),
            new IspWebhookIngestRequest("ixc-soft", "order.updated", ""));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task IngestWebhook_MalformedPayloadJson_Returns400()
    {
        using var server = await BuildServerAsync();
        using var client = server.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Authenticated", "true");

        var response = await client.PostAsJsonAsync(
            new Uri("/api/field-service/isp/webhooks/ingest", UriKind.Relative),
            new IspWebhookIngestRequest("ixc-soft", "order.updated", "{not valid json"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
