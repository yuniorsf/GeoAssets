using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Messaging.Events;
using GeoAssets.Core.Models;
using GeoAssets.FieldService.Server;
using GeoAssets.Infrastructure.Observability;
using GeoAssets.Workflow.FieldWork;
using GeoAssets.Workflow.Isp.Bss;
using GeoAssets.Workflow.Orders;
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
/// Integration coverage for XD01-280's order-spawn path on <c>IspModuleRestApiExtensions</c>
/// (the <c>EventType == "order.spawn"</c> branch added to the XD01-279 ingest shell) and for
/// the new <c>FtthDropInstallRestApiExtensions</c> transition endpoint — kept in one file since
/// both share the same FTTH drop-install order lifecycle and fake repository setup.
/// </summary>
public sealed class IspOrderSpawnAndFtthTransitionRoutingTests
{
    private const string IngestUri = "/api/field-service/isp/webhooks/ingest";
    private const string TransitionUriPrefix = "/api/field-service/isp/ftth-drop-install";

    private sealed class NoOpAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
            => Task.FromResult(AuthenticateResult.NoResult());
    }

    private sealed class FixedTenantContext(Guid organizationId) : ITenantContext
    {
        public Guid CurrentOrganizationId => organizationId;
    }

    /// <summary>
    /// None of this file's tests exercise <c>MapIspModuleApi</c>'s <c>geo-core/features/{id}</c>
    /// endpoint, but minimal-API endpoint metadata inference still needs a registered
    /// <see cref="IGeoCoreClient"/> to resolve that endpoint's service parameter when the
    /// endpoint data source is built — same reasoning as <c>IspModuleRoutingTests</c>'s own
    /// <c>FakeGeoCoreClient</c>.
    /// </summary>
    private sealed class NoOpGeoCoreClient : IGeoCoreClient
    {
        public Task<GeoFeature?> GetFeatureByIdAsync(string id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AssetBatchAppliedEvent> ExecuteAssetDeltaBatchAsync(AssetMutationBatchRequestedEvent batch, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PagedResult<GeoFeature>> QueryFeaturesAsync(AssetQuery query, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<string>> GetNeighborIdsAsync(string featureId, string? edgeKind = null, string? targetAssetTypeId = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> GetNeighborIdsBatchAsync(IReadOnlyList<string> featureIds, string? edgeKind = null, string? targetAssetTypeId = null, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class InMemoryServiceOrderRepository : IServiceOrderRepository
    {
        private readonly Dictionary<string, ServiceOrder> _orders = new(StringComparer.Ordinal);

        public IReadOnlyCollection<ServiceOrder> AllOrders => _orders.Values;

        public void Seed(ServiceOrder order) => _orders[order.Id] = order;

        public ServiceOrder? Find(string id) => _orders.GetValueOrDefault(id);

        public Task<IServiceOrder?> GetByIdAsync(string id, CancellationToken ct = default) =>
            Task.FromResult<IServiceOrder?>(_orders.GetValueOrDefault(id));

        public Task AddAsync(IServiceOrder order, CancellationToken ct = default)
        {
            _orders[order.Id] = (ServiceOrder)order;
            return Task.CompletedTask;
        }

        public Task UpdateAsync(IServiceOrder order, CancellationToken ct = default)
        {
            if (!_orders.ContainsKey(order.Id))
                throw new KeyNotFoundException($"ServiceOrder '{order.Id}' not found.");

            _orders[order.Id] = (ServiceOrder)order;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<IServiceOrder>> GetAllAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<IServiceOrder>> GetRootsAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<IServiceOrder>> GetChildrenAsync(string parentId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IServiceOrder?> GetParentAsync(string childId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<IServiceOrder>> GetByStatusAsync(string status, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<IServiceOrder>> GetByAssigneeAsync(string userId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<IServiceOrder>> GetByCreatorAsync(string userId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<IServiceOrder>> GetByOrderTypeAsync(string orderTypeId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<IServiceOrder>> GetByDateRangeAsync(DateTime from, DateTime toExclusive, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<IServiceOrder>> GetDispatchedToAsync(string targetId, DispatchTargetType targetType, CancellationToken ct = default) => throw new NotSupportedException();
        public Task AppendDispatchAsync(string orderId, OrderDispatch dispatch, CancellationToken ct = default) => throw new NotSupportedException();
        public Task AppendActionAsync(string orderId, OrderActionLog entry, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteAsync(string id, CancellationToken ct = default) => throw new NotSupportedException();

        public event EventHandler<IServiceOrder>? OrderAdded { add { } remove { } }
        public event EventHandler<IServiceOrder>? OrderUpdated { add { } remove { } }
        public event EventHandler<(IServiceOrder Order, string Previous)>? OrderStatusChanged { add { } remove { } }
        public event EventHandler<string>? OrderDeleted { add { } remove { } }
    }

    private sealed class FakeBssOssOutboundWebhookDispatcher(Exception? failure = null) : IBssOssOutboundWebhookDispatcher
    {
        public string? LastProviderId { get; private set; }
        public string? LastPayloadJson { get; private set; }
        public int CallCount { get; private set; }

        public Task DispatchAsync(string providerId, string payloadJson, CancellationToken ct = default)
        {
            CallCount++;
            LastProviderId = providerId;
            LastPayloadJson = payloadJson;
            if (failure is not null)
                throw failure;
            return Task.CompletedTask;
        }
    }

    private static async Task<TestServer> BuildServerAsync(
        InMemoryServiceOrderRepository repository, FakeBssOssOutboundWebhookDispatcher dispatcher, Guid organizationId)
    {
        var orderTypeRegistry = new OrderTypeRegistry();
        FtthDropInstallOrderTypes.RegisterAll(orderTypeRegistry);

        var host = await new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddSingleton<IGeoCoreClient, NoOpGeoCoreClient>();
                    services.AddSingleton<IServiceOrderRepository>(repository);
                    services.AddSingleton<IServiceOrderWriter>(repository);
                    services.AddSingleton(new BssOssOrderAdapterRegistry([new IxcSoftOrderAdapter()]));
                    services.AddSingleton<IBssOssOutboundWebhookDispatcher>(dispatcher);
                    services.AddSingleton<TimeProvider>(new FakeTimeProvider());
                    services.AddSingleton(orderTypeRegistry);
                    services.AddSingleton<ITenantContext>(new FixedTenantContext(organizationId));
                    services.AddSingleton<BssOrderSpawnService>();
                    services.AddSingleton<FtthDropInstallActivationDispatchService>();
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
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapIspModuleApi();
                        endpoints.MapFtthDropInstallApi();
                    });
                });
            })
            .StartAsync();

        return host.GetTestServer();
    }

    private static ServiceOrder OntProvisionedOrder(string id, Guid organizationId, string providerId = "IXC") => new()
    {
        Id = id,
        Status = FtthDropInstallStatus.OntProvisioned,
        OrganizationId = organizationId,
        Attributes =
        {
            [BssOrderAttributes.ProviderIdKey] = providerId,
            [BssOrderAttributes.SubscriberIdKey] = "cli-1",
            [FtthDropInstallChecklistAttributes.OntFsanKey] = "FSAN123",
            [FtthDropInstallChecklistAttributes.OntMacAddressKey] = "AA:BB:CC:DD:EE:FF",
        },
    };

    [Fact]
    public async Task IngestWebhook_WellFormedIxcOrderSpawn_Returns201AndPersistsOrder()
    {
        var organizationId = Guid.NewGuid();
        var repository = new InMemoryServiceOrderRepository();
        using var server = await BuildServerAsync(repository, new FakeBssOssOutboundWebhookDispatcher(), organizationId);
        using var client = server.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Authenticated", "true");

        var payloadJson = """{"id_cliente":"cli-1","nome":"Jane Doe","endereco":"Rua A, 123","id_plano":"plan-100"}""";
        var response = await client.PostAsJsonAsync(
            new Uri(IngestUri, UriKind.Relative),
            new IspWebhookIngestRequest("IXC", "order.spawn", payloadJson));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var order = repository.AllOrders.Should().ContainSingle().Subject;
        order.OrderTypeId.Should().Be(FtthDropInstallOrderTypes.FtthDropInstallId);
        order.Status.Should().Be(FtthDropInstallStatus.CrmSale);
        order.Attributes[BssOrderAttributes.SubscriberIdKey].Should().Be("cli-1");
        order.OrganizationId.Should().Be(organizationId);
    }

    [Fact]
    public async Task IngestWebhook_OrderSpawnUnknownProviderId_Returns400()
    {
        var repository = new InMemoryServiceOrderRepository();
        using var server = await BuildServerAsync(repository, new FakeBssOssOutboundWebhookDispatcher(), Guid.NewGuid());
        using var client = server.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Authenticated", "true");

        var response = await client.PostAsJsonAsync(
            new Uri(IngestUri, UriKind.Relative),
            new IspWebhookIngestRequest("UNKNOWN", "order.spawn", """{"id_cliente":"cli-1"}"""));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        repository.AllOrders.Should().BeEmpty();
    }

    [Fact]
    public async Task IngestWebhook_OrderSpawnPayloadMissingRequiredField_Returns400()
    {
        var repository = new InMemoryServiceOrderRepository();
        using var server = await BuildServerAsync(repository, new FakeBssOssOutboundWebhookDispatcher(), Guid.NewGuid());
        using var client = server.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Authenticated", "true");

        var payloadJson = """{"nome":"Jane Doe","endereco":"Rua A, 123","id_plano":"plan-100"}""";
        var response = await client.PostAsJsonAsync(
            new Uri(IngestUri, UriKind.Relative),
            new IspWebhookIngestRequest("IXC", "order.spawn", payloadJson));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        repository.AllOrders.Should().BeEmpty();
    }

    [Fact]
    public async Task IngestWebhook_EventTypeOrderUpdated_Returns202RegardlessOfProviderId()
    {
        var repository = new InMemoryServiceOrderRepository();
        using var server = await BuildServerAsync(repository, new FakeBssOssOutboundWebhookDispatcher(), Guid.NewGuid());
        using var client = server.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Authenticated", "true");

        var response = await client.PostAsJsonAsync(
            new Uri(IngestUri, UriKind.Relative),
            new IspWebhookIngestRequest("UNKNOWN-PROVIDER", "order.updated", """{"status":"completed"}"""));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task IngestWebhook_NoAuthenticatedPrincipal_Returns401()
    {
        var repository = new InMemoryServiceOrderRepository();
        using var server = await BuildServerAsync(repository, new FakeBssOssOutboundWebhookDispatcher(), Guid.NewGuid());
        using var client = server.CreateClient();

        var response = await client.PostAsJsonAsync(
            new Uri(IngestUri, UriKind.Relative),
            new IspWebhookIngestRequest("IXC", "order.updated", """{"status":"completed"}"""));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Transition_UnknownOrderId_Returns404()
    {
        var repository = new InMemoryServiceOrderRepository();
        using var server = await BuildServerAsync(repository, new FakeBssOssOutboundWebhookDispatcher(), Guid.NewGuid());
        using var client = server.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Authenticated", "true");

        var response = await client.PostAsJsonAsync(
            new Uri($"{TransitionUriPrefix}/missing-order/transition", UriKind.Relative),
            new FtthDropInstallTransitionRequest(FtthDropInstallStatus.CtoPortTest));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Transition_OrderBelongsToDifferentOrganization_Returns404()
    {
        var repository = new InMemoryServiceOrderRepository();
        repository.Seed(new ServiceOrder { Id = "order-1", Status = FtthDropInstallStatus.CrmSale, OrganizationId = Guid.NewGuid() });
        using var server = await BuildServerAsync(repository, new FakeBssOssOutboundWebhookDispatcher(), Guid.NewGuid());
        using var client = server.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Authenticated", "true");

        var response = await client.PostAsJsonAsync(
            new Uri($"{TransitionUriPrefix}/order-1/transition", UriKind.Relative),
            new FtthDropInstallTransitionRequest(FtthDropInstallStatus.CtoPortTest));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Transition_CrmSaleToCtoPortTest_Returns200WithNoDispatch()
    {
        var organizationId = Guid.NewGuid();
        var repository = new InMemoryServiceOrderRepository();
        repository.Seed(new ServiceOrder { Id = "order-1", Status = FtthDropInstallStatus.CrmSale, OrganizationId = organizationId });
        var dispatcher = new FakeBssOssOutboundWebhookDispatcher();
        using var server = await BuildServerAsync(repository, dispatcher, organizationId);
        using var client = server.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Authenticated", "true");

        var response = await client.PostAsJsonAsync(
            new Uri($"{TransitionUriPrefix}/order-1/transition", UriKind.Relative),
            new FtthDropInstallTransitionRequest(FtthDropInstallStatus.CtoPortTest));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<FtthDropInstallTransitionResponse>();
        body!.Status.Should().Be(FtthDropInstallStatus.CtoPortTest);
        body.ActivationWebhookDispatched.Should().BeFalse();
        dispatcher.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task Transition_ToActivatedWithValidIxcAttributes_Returns200AndDispatches()
    {
        var organizationId = Guid.NewGuid();
        var repository = new InMemoryServiceOrderRepository();
        repository.Seed(OntProvisionedOrder("order-1", organizationId));
        var dispatcher = new FakeBssOssOutboundWebhookDispatcher();
        using var server = await BuildServerAsync(repository, dispatcher, organizationId);
        using var client = server.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Authenticated", "true");

        var response = await client.PostAsJsonAsync(
            new Uri($"{TransitionUriPrefix}/order-1/transition", UriKind.Relative),
            new FtthDropInstallTransitionRequest(FtthDropInstallStatus.Activated));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<FtthDropInstallTransitionResponse>();
        body!.ActivationWebhookDispatched.Should().BeTrue();
        dispatcher.CallCount.Should().Be(1);
        dispatcher.LastProviderId.Should().Be("IXC");
    }

    [Fact]
    public async Task Transition_IllegalTransitionSkippingChain_Returns400()
    {
        var organizationId = Guid.NewGuid();
        var repository = new InMemoryServiceOrderRepository();
        repository.Seed(new ServiceOrder { Id = "order-1", Status = FtthDropInstallStatus.CrmSale, OrganizationId = organizationId });
        var dispatcher = new FakeBssOssOutboundWebhookDispatcher();
        using var server = await BuildServerAsync(repository, dispatcher, organizationId);
        using var client = server.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Authenticated", "true");

        var response = await client.PostAsJsonAsync(
            new Uri($"{TransitionUriPrefix}/order-1/transition", UriKind.Relative),
            new FtthDropInstallTransitionRequest(FtthDropInstallStatus.Activated));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        dispatcher.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task Transition_NoAuthenticatedPrincipal_Returns401()
    {
        var repository = new InMemoryServiceOrderRepository();
        using var server = await BuildServerAsync(repository, new FakeBssOssOutboundWebhookDispatcher(), Guid.NewGuid());
        using var client = server.CreateClient();

        var response = await client.PostAsJsonAsync(
            new Uri($"{TransitionUriPrefix}/order-1/transition", UriKind.Relative),
            new FtthDropInstallTransitionRequest(FtthDropInstallStatus.CtoPortTest));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
