using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Messaging;
using GeoAssets.FieldService.Server;
using GeoAssets.Identity.Authentication;
using GeoAssets.Workflow;
using GeoAssets.Workflow.Contractors;
using GeoAssets.Workflow.FieldWork;
using GeoAssets.Workflow.Messaging.Protobuf;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Persistence;
using GeoAssets.Workflow.Persistence.Outbox;
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
/// Integration coverage for <c>FieldWorkInspectionRestApiExtensions</c> (XD01-248) — the HTTP
/// façade and its tenant/error mapping specifically. <see cref="InspectionCommitService"/>'s own
/// ACID/optimistic-concurrency/catalog-not-found guarantees are already proven end-to-end against
/// a real transaction in <c>GeoAssets.Workflow.EFCore.Tests.InspectionCommitServiceTests</c> —
/// this file does not re-prove those, only that this endpoint reaches the service correctly and
/// maps its outcomes/exceptions to the right HTTP responses, including the tenant boundary
/// <c>InspectionCommitService</c> itself has no opinion on.
/// </summary>
public sealed class FieldWorkInspectionApiTests : IAsyncLifetime, IDisposable
{
    private const string OrganizationClaimType = "org_id";
    private static readonly Guid TenantA = Guid.Parse("77777777-7777-7777-7777-777777777777");
    private static readonly Guid TenantB = Guid.Parse("88888888-8888-8888-8888-888888888888");

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
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
                    services.AddScoped<IServiceOrderRepository>(sp =>
                        new ValidatingServiceOrderRepository(
                            new EFServiceOrderRepository(sp.GetRequiredService<ServiceOrderDbContext>(), orderTypeRegistry: sp.GetRequiredService<OrderTypeRegistry>()),
                            sp.GetRequiredService<OrderTypeRegistry>()));
                    services.AddScoped<IUsCatalogRepository, EFUsCatalogRepository>();
                    services.AddScoped<IContractorUsPostingRepository, EFContractorUsPostingRepository>();
                    services.AddSingleton(TimeProvider.System);
                    services.AddScoped<IOutboxWriter>(sp => new EFOutboxStore(
                        sp.GetRequiredService<ServiceOrderDbContext>(), sp.GetRequiredService<TimeProvider>(),
                        new ProtobufIntegrationMessageSerializer(), "test-service"));
                    services.AddScoped<InspectionCommitService>();
                });
                webHost.Configure(app =>
                {
                    using (var scope = app.ApplicationServices.CreateScope())
                    {
                        var db = scope.ServiceProvider.GetRequiredService<ServiceOrderDbContext>();
                        db.Database.EnsureCreated();

                        // Same SQLite RowVersion-on-UPDATE trigger as FieldServiceSyncApiTests —
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
                                [new Claim(OrganizationClaimType, orgId.ToString()), new Claim(ClaimTypes.NameIdentifier, "inspector-1")], "TestScheme"));
                        await next();
                    });
                    app.UseRouting();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints => endpoints.MapFieldWorkInspectionApi());
                });
            })
            .StartAsync();

        _server = host.GetTestServer();
    }

    private async Task<ServiceOrder> SeedCompletedOrderAsync(Guid organizationId, string id, string boqLinesJson)
    {
        using var scope = _server.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IServiceOrderRepository>();

        var order = new ServiceOrder
        {
            Id = id,
            Title = $"Order {id}",
            OrderTypeId = FieldWorkOrderTypes.FieldWorkId,
            CreatedBy = "tech-1",
            Status = FieldWorkStatus.Completed,
            OrganizationId = organizationId,
            CreatedAt = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
            Attributes = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [ServiceOrderBoqAttributes.ContractorOrganizationIdKey] = organizationId.ToString(),
                [ServiceOrderBoqAttributes.BoqLinesKey] = boqLinesJson,
            },
        };
        await repo.AddAsync(order).ConfigureAwait(false);
        return (ServiceOrder)(await repo.GetByIdAsync(id).ConfigureAwait(false))!;
    }

    private async Task SeedCatalogItemAsync(Guid organizationId, string itemCode)
    {
        using var scope = _server.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IUsCatalogRepository>();
        await repo.AddAsync(new UsCatalogItem
        {
            OrganizationId = organizationId,
            ItemCode = itemCode,
            BaseUsFactor = 2.5m,
            RegionalMultiplier = 1.2m,
            MonetaryConversionRate = 85m,
            EffectiveFrom = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        }).ConfigureAwait(false);
        await repo.SaveChangesAsync().ConfigureAwait(false);
    }

    private HttpClient CreateClient(Guid tenant)
    {
        var client = _server.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Org-Id", tenant.ToString());
        return client;
    }

    private static Uri InspectUri(string orderId) => new($"/api/field-service/orders/{orderId}/inspect", UriKind.Relative);

    [Fact]
    public async Task Inspect_HappyPath_AdvancesStatusAndPostsLedger()
    {
        await SeedCatalogItemAsync(TenantA, "US-EL-014");
        var order = await SeedCompletedOrderAsync(TenantA, "order-1", """[{"itemCode":"US-EL-014","quantity":2}]""");

        using var client = CreateClient(TenantA);
        var response = await client.PostAsJsonAsync(InspectUri("order-1"),
            new InspectServiceOrderRequest(order.RowVersion, "catalog-primary"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<InspectionCommitResponse>();
        result!.PostingCount.Should().Be(1);
        result.TotalUs.Should().Be(2m * 2.5m * 1.2m);
    }

    [Fact]
    public async Task Inspect_UnknownOrderId_Returns404()
    {
        using var client = CreateClient(TenantA);
        var response = await client.PostAsJsonAsync(InspectUri("does-not-exist"),
            new InspectServiceOrderRequest(new byte[8], "catalog-primary"));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Inspect_DifferentTenantsOrder_Returns404NotDisclosingExistence()
    {
        await SeedCatalogItemAsync(TenantA, "US-EL-014");
        var order = await SeedCompletedOrderAsync(TenantA, "order-1", """[{"itemCode":"US-EL-014","quantity":2}]""");

        using var attackerClient = CreateClient(TenantB);
        var response = await attackerClient.PostAsJsonAsync(InspectUri("order-1"),
            new InspectServiceOrderRequest(order.RowVersion, "catalog-primary"));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Inspect_StaleRowVersion_Returns409AndLeavesStateUnchanged()
    {
        await SeedCatalogItemAsync(TenantA, "US-EL-014");
        var order = await SeedCompletedOrderAsync(TenantA, "order-1", """[{"itemCode":"US-EL-014","quantity":2}]""");
        var staleRowVersion = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };

        using var client = CreateClient(TenantA);
        var response = await client.PostAsJsonAsync(InspectUri("order-1"),
            new InspectServiceOrderRequest(staleRowVersion, "catalog-primary"));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        using var scope = _server.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IServiceOrderRepository>();
        var reloaded = await repo.GetByIdAsync("order-1");
        reloaded!.Status.Should().Be(FieldWorkStatus.Completed, "a rejected commit must never advance the order's status");

        var postings = await scope.ServiceProvider.GetRequiredService<IContractorUsPostingRepository>().GetByServiceOrderIdAsync("order-1");
        postings.Should().BeEmpty("a rejected commit must leave zero ledger rows");
    }

    [Fact]
    public async Task Inspect_MissingCatalogItem_Returns400AndLeavesStateUnchanged()
    {
        var order = await SeedCompletedOrderAsync(TenantA, "order-1", """[{"itemCode":"NO-SUCH-ITEM","quantity":1}]""");

        using var client = CreateClient(TenantA);
        var response = await client.PostAsJsonAsync(InspectUri("order-1"),
            new InspectServiceOrderRequest(order.RowVersion, "catalog-primary"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var scope = _server.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IServiceOrderRepository>();
        var reloaded = await repo.GetByIdAsync("order-1");
        reloaded!.Status.Should().Be(FieldWorkStatus.Completed);
    }

    [Fact]
    public async Task Inspect_NoAuthenticatedPrincipal_Returns401()
    {
        using var client = _server.CreateClient();

        var response = await client.PostAsJsonAsync(InspectUri("order-1"), new InspectServiceOrderRequest(new byte[8], "catalog-primary"));

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
