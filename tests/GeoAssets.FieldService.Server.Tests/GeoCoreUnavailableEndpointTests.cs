using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.FieldService.Persistence;
using GeoAssets.FieldService.Server;
using GeoAssets.Identity.Authentication;
using GeoAssets.Integrations.Scada;
using GeoAssets.Workflow;
using GeoAssets.Workflow.Agents;
using GeoAssets.FieldService.Contractors;
using GeoAssets.FieldService.FieldWork;
using GeoAssets.Workflow.Agents.Dispatch;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace GeoAssets.FieldService.Server.Tests;

/// <summary>
/// XD01-386 consequences over HTTP: when the Core GeoAssets Server can't be read, the SCADA
/// breaker-trip endpoint and the dynamic-dispatch advisory endpoint answer a <c>503</c>
/// <c>geocore.unavailable</c> problem — except when the order could still be created, which is
/// a <c>2xx</c> flagged <c>isDegraded</c>. Real <see cref="FeederOutageDispatchService"/> /
/// <see cref="DynamicCrewDispatchAdvisorService"/> over a failable <see cref="FakeAssetTopologyQuery"/>
/// and an EF Core repository on SQLite; same harness shape as <see cref="FieldWorkTransitionApiTests"/>.
/// </summary>
public sealed class GeoCoreUnavailableEndpointTests : IAsyncLifetime, IDisposable
{
    private const string OrganizationClaimType = "org_id";
    private static readonly Guid Tenant = Guid.Parse("77777777-7777-7777-7777-777777777777");
    private static readonly Uri ScadaUri = new("/api/geoassets/topology/traverse", UriKind.Relative);
    private static readonly Uri AdvisoryUri = new("/api/geoassets/dispatch/advisory-plan", UriKind.Relative);

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly FakeAssetTopologyQuery _assets = new();
    private TestServer _server = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        var host = await new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddAuthentication("Test")
                        .AddScheme<AuthenticationSchemeOptions, NoOpAuthenticationHandler>("Test", _ => { });
                    services.AddAuthorization(options =>
                        options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
                    services.AddHttpContextAccessor();
                    services.AddScoped<ICurrentUserAccessor>(sp =>
                        new ClaimsPrincipalCurrentUserAccessor(
                            () => sp.GetRequiredService<IHttpContextAccessor>().HttpContext?.User,
                            new ClaimMapping
                            {
                                ObjectIdClaimTypes = [ClaimTypes.NameIdentifier],
                                OrganizationIdClaimTypes = [OrganizationClaimType],
                            }));
                    services.AddScoped<ITenantContext, HttpTenantContext>();
                    services.AddSingleton(TimeProvider.System);

                    services.AddOrderTypeRegistry(FieldWorkOrderTypes.RegisterAll);
                    services.AddDbContext<ServiceOrderDbContext>(options => options
                        .UseSqlite(_connection)
                        .ReplaceService<IModelCustomizer, RowVersionDefaultModelCustomizer>());
                    services.AddScoped<IServiceOrderRepository>(sp => new EFServiceOrderRepository(
                        sp.GetRequiredService<ServiceOrderDbContext>(),
                        orderTypeRegistry: sp.GetRequiredService<OrderTypeRegistry>(),
                        timeProvider: sp.GetRequiredService<TimeProvider>()));
                    // The US catalog store lives in FieldServiceDbContext since XD01-381, over the kernel context's connection.
                    services.AddFieldServicePersistence((options, connection) => options.UseSqlite(connection));

                    services.AddSingleton<IAssetTopologyQuery>(_assets);
                    services.AddSingleton<IDispatchAssetQuery>(_assets);
                    services.AddScoped<IFeederOutageDispatchService, FeederOutageDispatchService>();
                    services.AddDynamicCrewDispatchAdvisor();
                });
                webHost.Configure(app =>
                {
                    using (var scope = app.ApplicationServices.CreateScope())
                    {
                        var db = scope.ServiceProvider.GetRequiredService<ServiceOrderDbContext>();
                        db.Database.EnsureCreated();
                        FieldServiceSqliteSchema.Create(scope.ServiceProvider.GetRequiredService<FieldServiceDbContext>());
                        db.Database.ExecuteSqlRaw("""
                            CREATE TRIGGER ServiceOrders_RowVersion_Update AFTER UPDATE ON ServiceOrders
                            BEGIN
                                UPDATE ServiceOrders SET RowVersion = randomblob(8) WHERE rowid = NEW.rowid;
                            END;
                            """);
                    }

                    app.Use(async (ctx, next) =>
                    {
                        if (ctx.Request.Headers.TryGetValue("X-Test-Org-Id", out var orgId))
                            ctx.User = new ClaimsPrincipal(new ClaimsIdentity(
                                [new Claim(OrganizationClaimType, orgId.ToString()), new Claim(ClaimTypes.NameIdentifier, "oms-1")], "TestScheme"));
                        await next();
                    });
                    app.UseRouting();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapScadaTopologyTraverseApi();
                        endpoints.MapDynamicDispatchAdvisoryApi();
                    });
                });
            })
            .StartAsync();

        _server = host.GetTestServer();
    }

    private sealed class NoOpAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.NoResult());
    }

    private HttpClient CreateClient()
    {
        var client = _server.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Org-Id", Tenant.ToString());
        return client;
    }

    private static GeoFeature Feature(string id, string assetTypeId, GeoGeometry? geometry = null, Dictionary<string, string>? attributes = null) => new()
    {
        Id = id,
        Geometry = geometry,
        Properties = new GeoFeatureProperties
        {
            Name = id,
            AssetTypeId = assetTypeId,
            CustomAttributes = attributes ?? new Dictionary<string, string>(StringComparer.Ordinal),
        },
    };

    private void SeedHealthyNetwork()
    {
        var location = new GeoPoint(-43.2, -22.9);
        _assets.Features["sw1"] = Feature("sw1", "switch", location);
        var consumer = Feature("d1", "consumer-unit");
        _assets.Features["d1"] = consumer;
        _assets.Descendants["sw1"] = [consumer];
        _assets.Features["crew-1"] = Feature(
            "crew-1", ScadaOutageOrderTypes.CrewAssetTypeId, new GeoPoint(-43.2002, -22.9002),
            new Dictionary<string, string>(StringComparer.Ordinal) { [ScadaOutageOrderTypes.EmergencyQualifiedAttributeKey] = "true" });
    }

    private static async Task AssertGeoCoreUnavailableProblemAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("type").GetString().Should().Be("geocore.unavailable");
        problem.RootElement.GetProperty("code").GetString().Should().Be("geocore.unavailable");
        problem.RootElement.GetProperty("status").GetInt32().Should().Be(503);
    }

    private async Task<int> OrderCountAsync()
    {
        await using var scope = _server.Services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<IServiceOrderRepository>().GetAllAsync()).Count;
    }

    // ── SCADA breaker-trip ingest ──────────────────────────────────────────────

    [Fact]
    public async Task Scada_HealthyCore_Returns200WithoutDegradation()
    {
        SeedHealthyNetwork();

        using var client = CreateClient();
        var response = await client.PostAsJsonAsync(ScadaUri, new { trippedSwitchFeatureId = "sw1" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("isDegraded").GetBoolean().Should().BeFalse();
        body.RootElement.GetProperty("degradedReason").ValueKind.Should().Be(JsonValueKind.Null);
        body.RootElement.GetProperty("serviceOrderId").GetString().Should().NotBeNullOrEmpty();
        body.RootElement.GetProperty("impactedFeatureIds").EnumerateArray().Select(e => e.GetString()).Should().Equal("d1");
        body.RootElement.GetProperty("dispatchedCrewFeatureId").GetString().Should().Be("crew-1");
        (await OrderCountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Scada_SwitchUnreadable_Returns503ProblemAndCreatesNoOrder()
    {
        SeedHealthyNetwork();
        _assets.FeatureFailure = new GeoCoreUnavailableException("core down");

        using var client = CreateClient();
        var response = await client.PostAsJsonAsync(ScadaUri, new { trippedSwitchFeatureId = "sw1" });

        await AssertGeoCoreUnavailableProblemAsync(response);
        (await OrderCountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Scada_DescendantsUnreadable_Returns2xxWithIsDegradedTrueAndAnOrder()
    {
        SeedHealthyNetwork();
        _assets.DescendantsFailure = new GeoCoreUnavailableException("topology down");

        using var client = CreateClient();
        var response = await client.PostAsJsonAsync(ScadaUri, new { trippedSwitchFeatureId = "sw1" });

        response.IsSuccessStatusCode.Should().BeTrue();
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("isDegraded").GetBoolean().Should().BeTrue();
        body.RootElement.GetProperty("degradedReason").GetString().Should().Contain("Downstream impact could not be resolved");
        body.RootElement.GetProperty("serviceOrderId").GetString().Should().NotBeNullOrEmpty();
        body.RootElement.GetProperty("impactedFeatureIds").GetArrayLength().Should().Be(0);
        (await OrderCountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Scada_CrewRosterUnreadable_Returns2xxIsDegradedAndOrderUndispatched()
    {
        SeedHealthyNetwork();
        _assets.ByAssetTypeFailure = new GeoCoreUnavailableException("roster down");

        using var client = CreateClient();
        var response = await client.PostAsJsonAsync(ScadaUri, new { trippedSwitchFeatureId = "sw1" });

        response.IsSuccessStatusCode.Should().BeTrue();
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("isDegraded").GetBoolean().Should().BeTrue();
        body.RootElement.GetProperty("dispatchedCrewFeatureId").ValueKind.Should().Be(JsonValueKind.Null);
        body.RootElement.GetProperty("impactedFeatureIds").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task Scada_SwitchDoesNotExist_Returns404NotAProblem()
    {
        using var client = CreateClient();
        var response = await client.PostAsJsonAsync(ScadaUri, new { trippedSwitchFeatureId = "missing" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await OrderCountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Scada_NoAuthenticatedPrincipal_Returns401()
    {
        using var client = _server.CreateClient();

        var response = await client.PostAsJsonAsync(ScadaUri, new { trippedSwitchFeatureId = "sw1" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Dynamic dispatch advisory ──────────────────────────────────────────────

    [Fact]
    public async Task Advisory_HealthyCore_Returns200()
    {
        using var client = CreateClient();
        var response = await client.GetAsync(AdvisoryUri);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Advisory_CrewRosterUnreadable_Returns503Problem()
    {
        _assets.ByAssetTypeFailure = new GeoCoreUnavailableException("roster down");

        using var client = CreateClient();
        var response = await client.GetAsync(AdvisoryUri);

        await AssertGeoCoreUnavailableProblemAsync(response);
    }

    public Task DisposeAsync()
    {
        Dispose();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _server.Dispose();
        _connection.Dispose();
    }
}
