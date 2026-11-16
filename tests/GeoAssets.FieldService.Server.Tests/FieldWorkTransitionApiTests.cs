using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.FieldService.Server;
using GeoAssets.Identity.Authentication;
using GeoAssets.Workflow;
using GeoAssets.Workflow.Contractors;
using GeoAssets.Workflow.FieldWork;
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
/// Integration coverage for <c>FieldWorkTransitionRestApiExtensions</c> (XD01-295) — the HTTP
/// façade, its tenant/concurrency/error mapping, and the bypass-prevention check for
/// Inspected/ConflictReview. <c>FieldWorkOrderTransitioner</c>/<c>CompositeFieldWorkTransitionGuard</c>'s
/// own guard logic is already proven end-to-end in
/// <c>GeoAssets.Workflow.Tests.FieldWork.FieldWorkOrderTransitionerTests</c>/
/// <c>CompositeFieldWorkTransitionGuardTests</c> — this file does not re-prove that, only that
/// this endpoint actually reaches it and maps outcomes to the right HTTP responses.
/// Same test-harness shape as <see cref="FieldWorkInspectionApiTests"/>.
/// </summary>
public sealed class FieldWorkTransitionApiTests : IAsyncLifetime, IDisposable
{
    private const string OrganizationClaimType = "org_id";
    private static readonly Guid TenantA = Guid.Parse("77777777-7777-7777-7777-777777777777");
    private static readonly Guid TenantB = Guid.Parse("88888888-8888-8888-8888-888888888888");
    private static readonly GeoPoint AssetLocation = new(-46.633, -23.55);

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly TestAssetProvider _assetProvider = new();
    private TestServer _server = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync().ConfigureAwait(false);
        await BuildServerAsync().ConfigureAwait(false);
    }

    private sealed class NoOpAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
            => Task.FromResult(AuthenticateResult.NoResult());
    }

    private async Task BuildServerAsync()
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
                    services.AddAuthorization(options =>
                        options.FallbackPolicy = new AuthorizationPolicyBuilder()
                            .RequireAuthenticatedUser()
                            .Build());

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

                    services.AddOrderTypeRegistry(FieldWorkOrderTypes.RegisterAll);
                    services.AddDbContext<ServiceOrderDbContext>(options => options
                        .UseSqlite(_connection)
                        .ReplaceService<IModelCustomizer, RowVersionDefaultModelCustomizer>());
                    services.AddSingleton<IAssetProvider>(_assetProvider);
                    services.AddScoped<IServiceOrderRepository>(sp =>
                        new ValidatingServiceOrderRepository(
                            new EFServiceOrderRepository(sp.GetRequiredService<ServiceOrderDbContext>(), sp.GetRequiredService<IAssetProvider>(), sp.GetRequiredService<OrderTypeRegistry>()),
                            sp.GetRequiredService<OrderTypeRegistry>()));
                    services.AddSingleton(TimeProvider.System);
                    services.AddSingleton<PhotoGatingRuleRegistry>();
                    services.AddScoped<GeofenceArrivalTransitionGuard>();
                    services.AddScoped<SafetyChecklistTransitionGuard>();
                    services.AddScoped<PhotoGatingTransitionGuard>();
                    services.AddScoped<IFieldWorkTransitionGuard>(sp => new CompositeFieldWorkTransitionGuard(
                        sp.GetRequiredService<GeofenceArrivalTransitionGuard>(),
                        sp.GetRequiredService<SafetyChecklistTransitionGuard>(),
                        sp.GetRequiredService<PhotoGatingTransitionGuard>()));
                });
                webHost.Configure(app =>
                {
                    using (var scope = app.ApplicationServices.CreateScope())
                    {
                        var db = scope.ServiceProvider.GetRequiredService<ServiceOrderDbContext>();
                        db.Database.EnsureCreated();

                        // Same SQLite RowVersion-on-UPDATE trigger as FieldWorkInspectionApiTests —
                        // SQLite has no native auto-updating rowversion column.
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
                                [new Claim(OrganizationClaimType, orgId.ToString()), new Claim(ClaimTypes.NameIdentifier, "crew-1")], "TestScheme"));
                        await next();
                    });
                    app.UseRouting();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints => endpoints.MapFieldWorkTransitionApi());
                });
            })
            .StartAsync();

        _server = host.GetTestServer();
    }

    private async Task<ServiceOrder> SeedOrderAsync(Guid organizationId, string id, string status, string orderTypeId = FieldWorkOrderTypes.FieldWorkId, string? targetFeatureId = null)
    {
        using var scope = _server.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IServiceOrderRepository>();

        var order = new ServiceOrder
        {
            Id = id,
            Title = $"Order {id}",
            OrderTypeId = orderTypeId,
            CreatedBy = "crew-1",
            Status = status,
            OrganizationId = organizationId,
            CreatedAt = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
            Features = targetFeatureId is null ? [] : [new GeoFeature { Id = targetFeatureId, Geometry = AssetLocation }],
        };
        await repo.AddAsync(order).ConfigureAwait(false);
        return (ServiceOrder)(await repo.GetByIdAsync(id).ConfigureAwait(false))!;
    }

    private HttpClient CreateClient(Guid tenant)
    {
        var client = _server.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Org-Id", tenant.ToString());
        return client;
    }

    private static Uri TransitionUri(string orderId) => new($"/api/field-service/orders/{orderId}/field-work-transition", UriKind.Relative);

    private sealed record Request(string NewStatus, byte[] RowVersion, Dictionary<string, string>? Attributes = null, double? DeviceLongitude = null, double? DeviceLatitude = null);

    [Fact]
    public async Task UnguardedEdge_DispatchedToEnRoute_Succeeds()
    {
        var order = await SeedOrderAsync(TenantA, "order-1", FieldWorkStatus.Dispatched);

        using var client = CreateClient(TenantA);
        var response = await client.PostAsJsonAsync(TransitionUri("order-1"), new Request(FieldWorkStatus.EnRoute, order.RowVersion));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GeofenceGuardedEdge_DeviceWithinTolerance_Succeeds()
    {
        _assetProvider.Add(new GeoFeature { Id = "asset-1", Geometry = AssetLocation });
        var order = await SeedOrderAsync(TenantA, "order-1", FieldWorkStatus.EnRoute, targetFeatureId: "asset-1");

        using var client = CreateClient(TenantA);
        var response = await client.PostAsJsonAsync(TransitionUri("order-1"),
            new Request(FieldWorkStatus.OnSite, order.RowVersion, DeviceLongitude: AssetLocation.Longitude, DeviceLatitude: AssetLocation.Latitude));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GeofenceGuardedEdge_DeviceFarAway_Returns400()
    {
        _assetProvider.Add(new GeoFeature { Id = "asset-1", Geometry = AssetLocation });
        var order = await SeedOrderAsync(TenantA, "order-1", FieldWorkStatus.EnRoute, targetFeatureId: "asset-1");

        using var client = CreateClient(TenantA);
        var response = await client.PostAsJsonAsync(TransitionUri("order-1"),
            new Request(FieldWorkStatus.OnSite, order.RowVersion, DeviceLongitude: AssetLocation.Longitude, DeviceLatitude: AssetLocation.Latitude + 1));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GeofenceGuardedEdge_MissingDevicePoint_Returns400()
    {
        var order = await SeedOrderAsync(TenantA, "order-1", FieldWorkStatus.EnRoute);

        using var client = CreateClient(TenantA);
        var response = await client.PostAsJsonAsync(TransitionUri("order-1"), new Request(FieldWorkStatus.OnSite, order.RowVersion));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SafetyGuardedEdge_UnrestrictedOrderType_StartsWorkWithNoAttributes()
    {
        var order = await SeedOrderAsync(TenantA, "order-1", FieldWorkStatus.OnSite);

        using var client = CreateClient(TenantA);
        var response = await client.PostAsJsonAsync(TransitionUri("order-1"), new Request(FieldWorkStatus.InProgress, order.RowVersion));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Proves the endpoint's own "merge Attributes into the order before evaluating the
    /// transition" mechanics, independent of any gate — a real, schema-gated checklist type
    /// (e.g. <c>SafetyChecklistSchemas.BuildElectricalOrderType</c>) can't be exercised
    /// end-to-end here: <c>ValidatingServiceOrderRepository.AddAsync</c>/<c>UpdateAsync</c>
    /// validate the full attribute schema on every write including the seed below, so this test
    /// deliberately uses the plain, schema-less registered type (see <c>Program.cs</c>'s own
    /// note on why no schema-gated type is registered on this host) and asserts only that the
    /// submitted attribute round-trips, not that any gate reacted to it.
    /// </summary>
    [Fact]
    public async Task SubmittedAttributes_AreMergedAndPersistedBeforeTransition()
    {
        var order = await SeedOrderAsync(TenantA, "order-1", FieldWorkStatus.OnSite);
        var attributes = new Dictionary<string, string>(StringComparer.Ordinal) { ["checklistSubmittedAtUtc"] = "2026-11-10T12:00:00Z" };

        using var client = CreateClient(TenantA);
        var response = await client.PostAsJsonAsync(TransitionUri("order-1"), new Request(FieldWorkStatus.InProgress, order.RowVersion, attributes));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _server.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IServiceOrderRepository>();
        var reloaded = await repo.GetByIdAsync("order-1");
        reloaded!.Attributes.Should().Contain("checklistSubmittedAtUtc", "2026-11-10T12:00:00Z");
    }

    [Theory]
    [InlineData(FieldWorkStatus.Inspected)]
    [InlineData(FieldWorkStatus.ConflictReview)]
    public async Task SupervisorOnlyStatus_Returns400(string status)
    {
        var order = await SeedOrderAsync(TenantA, "order-1", FieldWorkStatus.Completed);

        using var client = CreateClient(TenantA);
        var response = await client.PostAsJsonAsync(TransitionUri("order-1"), new Request(status, order.RowVersion));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var scope = _server.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IServiceOrderRepository>();
        var reloaded = await repo.GetByIdAsync("order-1");
        reloaded!.Status.Should().Be(FieldWorkStatus.Completed, "the bypass-prevention check must reject before any repository write");
    }

    [Fact]
    public async Task UnknownOrderId_Returns404()
    {
        using var client = CreateClient(TenantA);
        var response = await client.PostAsJsonAsync(TransitionUri("does-not-exist"), new Request(FieldWorkStatus.EnRoute, new byte[8]));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DifferentTenantsOrder_Returns404NotDisclosingExistence()
    {
        var order = await SeedOrderAsync(TenantA, "order-1", FieldWorkStatus.Dispatched);

        using var attackerClient = CreateClient(TenantB);
        var response = await attackerClient.PostAsJsonAsync(TransitionUri("order-1"), new Request(FieldWorkStatus.EnRoute, order.RowVersion));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task StaleRowVersion_Returns409AndLeavesStateUnchanged()
    {
        await SeedOrderAsync(TenantA, "order-1", FieldWorkStatus.Dispatched);
        var staleRowVersion = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };

        using var client = CreateClient(TenantA);
        var response = await client.PostAsJsonAsync(TransitionUri("order-1"), new Request(FieldWorkStatus.EnRoute, staleRowVersion));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        using var scope = _server.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IServiceOrderRepository>();
        var reloaded = await repo.GetByIdAsync("order-1");
        reloaded!.Status.Should().Be(FieldWorkStatus.Dispatched);
    }

    [Fact]
    public async Task StructurallyIllegalEdge_Returns400()
    {
        var order = await SeedOrderAsync(TenantA, "order-1", FieldWorkStatus.Draft);

        using var client = CreateClient(TenantA);
        var response = await client.PostAsJsonAsync(TransitionUri("order-1"), new Request(FieldWorkStatus.Completed, order.RowVersion));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task NoAuthenticatedPrincipal_Returns401()
    {
        using var client = _server.CreateClient();

        var response = await client.PostAsJsonAsync(TransitionUri("order-1"), new Request(FieldWorkStatus.EnRoute, new byte[8]));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
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
