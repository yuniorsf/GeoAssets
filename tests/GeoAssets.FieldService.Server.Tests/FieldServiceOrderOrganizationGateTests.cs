using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Messaging.Events;
using GeoAssets.Core.Models;
using GeoAssets.FieldService.Persistence;
using GeoAssets.Identity.Authentication;
using GeoAssets.FieldService.Contractors;
using GeoAssets.FieldService.Contractors.Governance;
using GeoAssets.FieldService.FieldWork;
using GeoAssets.NeutralOperator.Application;
using GeoAssets.Workflow;
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
/// XD01-332 on the FieldService host, which reaches orders without <c>ServiceOrderRules</c>:
/// every route that takes an order id serves it only to the order's own tenant and answers
/// another tenant exactly as for an unknown id; the one order this host creates is created
/// under the caller's own tenant; and a push never moves an order to another organization.
/// The routes that already checked the tenant (sync push, field-work transition and inspection,
/// FTTH transition, SAP PM confirmation) keep the tests they had.
/// </summary>
public sealed class FieldServiceOrderOrganizationGateTests : IAsyncLifetime, IDisposable
{
    private const string OrganizationClaimType = "org_id";
    private const string Secret = "tenant-secret";
    private static readonly Guid TenantA = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid TenantB = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Uri PushUri = new("/api/field-service/sync/service-orders", UriKind.Relative);
    private static readonly Uri PullUri = new("/api/field-service/sync/service-orders?sinceUtc=0001-01-01T00:00:00Z", UriKind.Relative);

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly RecordingGeoCoreClient _geoCore = new();
    private readonly RecordingPortalClient _portal = new();
    private TestServer _server = null!;

    private sealed class NoOpAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.NoResult());
    }

    private sealed class RecordingGeoCoreClient : IGeoCoreClient
    {
        public List<AssetMutationBatchRequestedEvent> AppliedBatches { get; } = [];

        public Task<GeoFeature?> GetFeatureByIdAsync(string id, CancellationToken ct = default) => Task.FromResult<GeoFeature?>(null);

        public Task<PagedResult<GeoFeature>> QueryFeaturesAsync(AssetQuery query, CancellationToken ct = default) =>
            Task.FromResult(new PagedResult<GeoFeature> { Items = [], TotalCount = 0 });

        public Task<IReadOnlyList<string>> GetNeighborIdsAsync(string featureId, string? edgeKind = null, string? targetAssetTypeId = null, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);

        public Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> GetNeighborIdsBatchAsync(IReadOnlyList<string> featureIds, string? edgeKind = null, string? targetAssetTypeId = null, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyDictionary<string, IReadOnlyList<string>>>(new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal));

        public Task<AssetBatchAppliedEvent> ExecuteAssetDeltaBatchAsync(AssetMutationBatchRequestedEvent batch, CancellationToken ct = default)
        {
            AppliedBatches.Add(batch);
            return Task.FromResult(AssetBatchAppliedEvent.Create(
                batch.BatchId, batch.CatalogInstanceId, batch.OrganizationId, batch.RequestedByService, batch.PerformedByUserId,
                appliedItems: [], timeProvider: TimeProvider.System, sourceOrderId: batch.SourceOrderId, sourceOrderTypeId: batch.SourceOrderTypeId));
        }
    }

    private sealed class RecordingPortalClient : IContractorGovernancePortalClient
    {
        public List<MeasurementSheetPortalPayload> Pushed { get; } = [];

        public Task PushMeasurementSheetAsync(MeasurementSheetPortalPayload payload, CancellationToken ct = default)
        {
            Pushed.Add(payload);
            return Task.CompletedTask;
        }

        public Task<ContractBalanceQueryResult> QueryContractBalanceAsync(string contractId, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

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
                    services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, NoOpAuthenticationHandler>("Test", _ => { });
                    services.AddAuthorization(options =>
                        options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

                    services.AddHttpContextAccessor();
                    services.AddScoped<ICurrentUserAccessor>(sp =>
                        new ClaimsPrincipalCurrentUserAccessor(
                            () => sp.GetRequiredService<IHttpContextAccessor>().HttpContext?.User,
                            new ClaimMapping { OrganizationIdClaimTypes = [OrganizationClaimType] }));
                    services.AddScoped<ITenantContext, HttpTenantContext>();

                    services.AddSingleton(TimeProvider.System);
                    services.AddDbContext<ServiceOrderDbContext>(options => options
                        .UseSqlite(_connection)
                        .ReplaceService<IModelCustomizer, RowVersionDefaultModelCustomizer>());
                    // As Program.cs registers it: the EF store behind the validating decorator.
                    services.AddScoped<IServiceOrderRepository>(sp =>
                        new ValidatingServiceOrderRepository(new EFServiceOrderRepository(sp.GetRequiredService<ServiceOrderDbContext>())));
                    // The ledger and postings stores live in FieldServiceDbContext since XD01-381, over the kernel context's connection.
                    services.AddFieldServicePersistence((options, connection) => options.UseSqlite(connection));
                    services.AddSingleton<IGeoCoreClient>(_geoCore);
                    services.AddSingleton<IContractorGovernancePortalClient>(_portal);
                    // As Program.cs registers it (XD01-328): the sync routes' cross-organization access, over empty mirrors.
                    services.AddServiceOrderRules();
                    services.AddScoped<IMirroredCrossOrganizationEntitlements, EFMirroredCrossOrganizationEntitlements>();
                    services.AddFieldServiceCrossOrganizationAccess();
                });
                webHost.Configure(app =>
                {
                    using (var scope = app.ApplicationServices.CreateScope())
                    {
                        scope.ServiceProvider.GetRequiredService<ServiceOrderDbContext>().Database.EnsureCreated();
                        FieldServiceSqliteSchema.Create(scope.ServiceProvider.GetRequiredService<FieldServiceDbContext>());
                    }

                    app.Use(async (ctx, next) =>
                    {
                        if (ctx.Request.Headers.TryGetValue("X-Test-Org-Id", out var orgId))
                        {
                            ctx.User = new ClaimsPrincipal(new ClaimsIdentity(
                                [new Claim(OrganizationClaimType, orgId.ToString())], "TestScheme"));
                        }

                        await next();
                    });
                    app.UseRouting();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapFieldServiceSyncApi();
                        endpoints.MapContractorUsLedgerApi();
                        endpoints.MapSapWeBuyClearingPackageApi();
                        endpoints.MapContractorGovernancePortalApi();
                    });
                });
            })
            .StartAsync();

        _server = host.GetTestServer();
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

    private HttpClient ClientOf(Guid tenant)
    {
        var client = _server.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Org-Id", tenant.ToString());
        return client;
    }

    private async Task SeedAsync(string id, Guid organizationId, string status = FieldWorkStatus.Inspected)
    {
        using var scope = _server.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IServiceOrderRepository>().AddAsync(new ServiceOrder
        {
            Id = id,
            Title = $"Order {id}",
            OrderTypeId = "type-a",
            CreatedBy = "tester",
            AssignedTo = "technician-of-a",
            Status = status,
            OrganizationId = organizationId,
            CreatedAt = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
        });
    }

    private async Task<string[]> PulledIdsAsync(Guid tenant)
    {
        using var client = ClientOf(tenant);
        var orders = await (await client.GetAsync(PullUri)).Content.ReadFromJsonAsync<List<ServiceOrder>>();
        return [.. orders!.Select(o => o.Id)];
    }

    private async Task<ServiceOrder> StoredAsync(string id)
    {
        using var scope = _server.Services.CreateScope();
        return (ServiceOrder)(await scope.ServiceProvider.GetRequiredService<IServiceOrderRepository>().GetByIdAsync(id))!;
    }

    // ── Reads by order id that used to skip the tenant check ───────────────────

    public static TheoryData<string> ByServiceOrderRoutes() => ["espelho-de-medicao-dossier", "sap-webuy clearing-package", "push-measurement-sheet"];

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string route, string orderId) => route switch
    {
        "espelho-de-medicao-dossier" => client.PostAsJsonAsync(
            new Uri($"/api/fieldservice/contractor-us-ledger/by-service-order/{orderId}/espelho-de-medicao-dossier", UriKind.Relative),
            new EspelhoDeMedicaoDossierRequest(Secret)),
        "sap-webuy clearing-package" => client.PostAsJsonAsync(
            new Uri($"/api/fieldservice/erp/sap-webuy/by-service-order/{orderId}/clearing-package", UriKind.Relative),
            new SapWeBuyExportClearingPackageApiRequest(Secret, "PO-1", "OA-1", "https://evidence.example/")),
        "push-measurement-sheet" => client.PostAsJsonAsync(
            new Uri($"/api/fieldservice/contractor-governance-portal/by-service-order/{orderId}/push-measurement-sheet", UriKind.Relative),
            new PushMeasurementSheetRequest("contract-1", Secret)),
        _ => throw new ArgumentOutOfRangeException(nameof(route), route, "Unknown route."),
    };

    [Theory]
    [MemberData(nameof(ByServiceOrderRoutes))]
    public async Task XD01_332_ByServiceOrderRoutes_AnotherTenantsOrder_GetsTheSame404AsAnUnknownOrder_AndDisclosesNothing(string route)
    {
        ArgumentNullException.ThrowIfNull(route);
        await SeedAsync("order-of-a", TenantA);
        using var asB = ClientOf(TenantB);

        var foreign = await PostAsync(asB, route, "order-of-a");
        var unknown = await PostAsync(asB, route, "no-such-order");

        foreign.StatusCode.Should().Be(HttpStatusCode.NotFound);
        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await foreign.Content.ReadAsStringAsync()).Should().Be(await unknown.Content.ReadAsStringAsync());
        _portal.Pushed.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(ByServiceOrderRoutes))]
    public async Task XD01_332_ByServiceOrderRoutes_TheOrdersOwnTenant_IsServedAsBefore(string route)
    {
        ArgumentNullException.ThrowIfNull(route);
        await SeedAsync("order-of-a", TenantA);
        using var asA = ClientOf(TenantA);

        if (string.Equals(route, "sap-webuy clearing-package", StringComparison.Ordinal))
        {
            // The order was found and its dossier compiled: the exporter then refuses an order
            // with no BoQ lines, which this seed has none of — as it did before this change.
            var failure = await Record.ExceptionAsync(async () => await PostAsync(asA, route, "order-of-a"));
            failure.Should().BeOfType<InvalidOperationException>().Which.Message.Should().Contain("'order-of-a'").And.Contain("no itemized BoQ lines");
        }
        else
        {
            var response = await PostAsync(asA, route, "order-of-a");
            response.StatusCode.Should().Be(HttpStatusCode.OK, "the body was: {0}", await response.Content.ReadAsStringAsync());
        }
    }

    // ── The one order this host creates ────────────────────────────────────────

    private static ServiceOrder Census(string id, Guid organizationId) => new()
    {
        Id = id,
        Title = "Pole census — pole-1",
        OrderTypeId = PoleCensusOrderTypes.PoleCensusInspectionId,
        CreatedBy = "tech-1",
        OrganizationId = organizationId,
        Features = [new GeoFeature { Id = "pole-1", Properties = new() { AssetTypeId = "neutral-pole" } }],
        Attributes = new(StringComparer.Ordinal)
        {
            ["structuralCondition"] = "Fair",
            ["band500mmCompliant"] = "true",
            ["clearanceMeasurements"] = """{"groundToLowestCableMeters": 4.8, "powerToCommunicationBandMeters": 0.55}""",
            ["occupants"] = """[{"slotIndex": 1, "status": "AuthorizedContracted", "operatorCnpj": "12345678000190"}]""",
            ["unauthorizedCableDetected"] = "false",
        },
    };

    [Fact]
    public async Task XD01_332_PushNewPoleCensus_StatingAnotherTenantsOrganization_IsRefused_AndNothingIsStoredOrSentToCore()
    {
        using var asA = ClientOf(TenantA);

        var response = await asA.PostAsJsonAsync(PushUri, new List<PushServiceOrderItem> { new(Census("census-x", TenantB), "idem-1") });

        var result = (await response.Content.ReadFromJsonAsync<List<PushServiceOrderResult>>())!.Should().ContainSingle().Subject;
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("own organization");
        (await PulledIdsAsync(TenantA)).Should().BeEmpty();
        (await PulledIdsAsync(TenantB)).Should().BeEmpty();
        _geoCore.AppliedBatches.Should().BeEmpty();
    }

    [Fact]
    public async Task XD01_332_PushNewPoleCensus_StatingNoOrganization_IsStoredUnderTheCallersOwnTenant()
    {
        using var asA = ClientOf(TenantA);

        var response = await asA.PostAsJsonAsync(PushUri, new List<PushServiceOrderItem> { new(Census("census-y", Guid.Empty), "idem-2") });

        (await response.Content.ReadFromJsonAsync<List<PushServiceOrderResult>>())!.Should().ContainSingle(r => r.Success);
        (await StoredAsync("census-y")).OrganizationId.Should().Be(TenantA);
        (await PulledIdsAsync(TenantA)).Should().Equal("census-y");
        _geoCore.AppliedBatches.Should().ContainSingle().Which.OrganizationId.Should().Be(TenantA);
    }

    // ── A push never moves an order ────────────────────────────────────────────

    [Fact]
    public async Task XD01_332_PushUpdate_StatingAnotherOrganizationForTheCallersOwnOrder_IsRefused_AndTheOrderStaysWhereItWas()
    {
        await SeedAsync("order-of-a", TenantA, ServiceOrderStatus.Draft);
        var existing = await StoredAsync("order-of-a");
        var relabelled = new ServiceOrder
        {
            Id = existing.Id,
            RowVersion = existing.RowVersion,
            Title = "moved",
            OrderTypeId = existing.OrderTypeId,
            CreatedBy = existing.CreatedBy,
            Status = existing.Status,
            OrganizationId = TenantB,
            CreatedAt = existing.CreatedAt,
        };
        using var asA = ClientOf(TenantA);

        var response = await asA.PostAsJsonAsync(PushUri, new List<PushServiceOrderItem> { new(relabelled, "idem-3") });

        var result = (await response.Content.ReadFromJsonAsync<List<PushServiceOrderResult>>())!.Should().ContainSingle().Subject;
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("organization");
        var stored = await StoredAsync("order-of-a");
        stored.OrganizationId.Should().Be(TenantA);
        stored.Title.Should().Be("Order order-of-a");
        (await PulledIdsAsync(TenantB)).Should().BeEmpty();
    }

    [Fact]
    public async Task XD01_332_PushUpdate_OfAnotherTenantsOrder_IsReportedAsNotFound_WhateverOrganizationTheBodyStates()
    {
        await SeedAsync("order-of-a", TenantA, ServiceOrderStatus.Draft);
        var existing = await StoredAsync("order-of-a");
        var asTheirs = new ServiceOrder
        {
            Id = existing.Id, RowVersion = existing.RowVersion, Title = "taken", OrderTypeId = existing.OrderTypeId,
            CreatedBy = existing.CreatedBy, Status = existing.Status, OrganizationId = TenantB, CreatedAt = existing.CreatedAt,
        };
        using var asB = ClientOf(TenantB);

        var foreign = await asB.PostAsJsonAsync(PushUri, new List<PushServiceOrderItem> { new(asTheirs, "idem-4") });
        var unknown = await asB.PostAsJsonAsync(PushUri, new List<PushServiceOrderItem>
        {
            new(new ServiceOrder { Id = "no-such-order", Title = "x", OrderTypeId = "type-a", CreatedBy = "tester", OrganizationId = TenantB }, "idem-5"),
        });

        var foreignResult = (await foreign.Content.ReadFromJsonAsync<List<PushServiceOrderResult>>())!.Single();
        var unknownResult = (await unknown.Content.ReadFromJsonAsync<List<PushServiceOrderResult>>())!.Single();
        foreignResult.Success.Should().BeFalse();
        foreignResult.Error.Should().Be(unknownResult.Error);
        (await StoredAsync("order-of-a")).Title.Should().Be("Order order-of-a");
    }
}
