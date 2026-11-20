using System.Net.Http.Headers;
using FluentAssertions;
using GeoAssets.Core.Messaging;
using GeoAssets.Core.Messaging.Events;
using GeoAssets.NeutralOperator.Application.Occupancy;
using GeoAssets.NeutralOperator.Application.Occupancy.Saga;
using GeoAssets.NeutralOperator.Application.SharedUse;
using GeoAssets.NeutralOperator.Domain;
using GeoAssets.NeutralOperator.Domain.Occupancy;
using GeoAssets.NeutralOperator.Rest;
using GeoAssets.NeutralOperator.Server.Tests.Support;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rest;
using GeoAssets.Workflow.Rules;
using GeoAssets.Workflow.Targets;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GeoAssets.Gateway.Tests.EndToEnd;

/// <summary>
/// XD01-384, the acceptance criterion: "Web performs shared-use create → target → dispatch → approve through the gateway".
/// The Web's own REST clients (<c>AddDescriptorRoutedWorkflowRest</c>, the registration its <c>Program</c> makes) talk only to the
/// gateway — the real <c>GeoAssets.Gateway</c> <c>Program</c> — which forwards the vertical's <c>/api/v/neutral-operator/**</c> to the
/// real <c>GeoAssets.NeutralOperator.Server</c> (over a Testcontainers database of its own, the kernel it calls replaced by an
/// in-process stand-in) and everything else to the kernel. The flow: the order is created, its poles selected (preview and apply)
/// into the kernel's ledger and the host's mirror, dispatched (one notice per occupant), completed, its occupancy staged, and
/// approved — the approval then waits for the kernel's apply, whose answer is delivered to the host's saga handler as the broker
/// would; the order is Approved. In the rollback configuration the gateway's NeutralOperator destination is the kernel itself.
/// </summary>
public sealed class GatewayEndToEndTests : IAsyncLifetime
{
    private const string OrderId = "e2e-shared-use-1";
    private const string VerticalRoot = "/api/v/neutral-operator/workflow/";
    private static readonly DateTime Now = new(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);
    private static readonly WorkflowPrincipal Principal = new("owner-admin", TestIdentities.OwnerOrganization.ToString(), ["Administrator"], [], []);

    private readonly List<IAsyncDisposable> _disposables = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var disposable in _disposables.AsEnumerable().Reverse()) await disposable.DisposeAsync();
    }

    private sealed class Stack(
        NeutralOperatorServerHost host, RecordingHandler kernelEdge, RecordingHandler neutralOperatorDestination, RecordingHandler webToGateway, HttpMessageHandler gatewayHandler, ServiceProvider web)
    {
        public NeutralOperatorServerHost Host { get; } = host;

        /// <summary>What reached the kernel destination of the gateway.</summary>
        public RecordingHandler Kernel { get; } = kernelEdge;

        /// <summary>What reached the NeutralOperator destination of the gateway.</summary>
        public RecordingHandler NeutralOperator { get; } = neutralOperatorDestination;

        /// <summary>What the Web's clients sent to the gateway.</summary>
        public RecordingHandler Web { get; } = webToGateway;

        /// <summary>The gateway's own handler, for a caller that is not the Web's clients.</summary>
        public HttpMessageHandler GatewayHandler { get; } = gatewayHandler;

        public ServiceProvider Clients { get; } = web;
    }

    private async Task<Stack> StartAsync(bool rollback)
    {
        var host = await NeutralOperatorServerHost.StartAsync();
        _disposables.Add(host);

        // What the vertical published to the kernel is what the kernel's registry serves the Web.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (host.Kernel.To(HttpMethod.Put, "/order-types").Count == 0) await Task.Delay(25, timeout.Token);
        var descriptors = host.Kernel.To(HttpMethod.Put, "/order-types")[^1].Body!;

        var verticalHost = new RecordingHandler(host.CreateHandler());
        var kernelEdge = new RecordingHandler(new KernelEdge(descriptors, rollback ? verticalHost : null));
        var verticalDestination = rollback ? "kernel.test" : "neutral-operator.test";
        var hosts = new Dictionary<string, HttpMessageHandler>(StringComparer.Ordinal) { ["kernel.test"] = kernelEdge, ["neutral-operator.test"] = verticalHost };

        var gateway = GatewayFactory.ForAddresses(
            "http://kernel.test", $"http://{verticalDestination}", services => services.AddSingleton<Yarp.ReverseProxy.Forwarder.IForwarderHttpClientFactory>(new HostRoutingForwarder(hosts)));
        _disposables.Add(gateway);

        var gatewayHandler = gateway.Server.CreateHandler();
        var webToGateway = new RecordingHandler(gatewayHandler);
        var services = new ServiceCollection();
        services.AddHttpClient("GeoAssetsServer")
            .ConfigurePrimaryHttpMessageHandler(() => webToGateway)
            .AddHttpMessageHandler(() => new BearerHandler("owner-admin"));
        services.AddDescriptorRoutedWorkflowRest("http://gateway.test/api/workflow");
        services.AddDescriptorRoutedNeutralOperatorRest();
        var web = services.BuildServiceProvider();
        _disposables.Add(web);

        return new Stack(host, kernelEdge, rollback ? kernelEdge : verticalHost, webToGateway, gatewayHandler, web);
    }

    private sealed class BearerHandler(string token) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return base.SendAsync(request, cancellationToken);
        }
    }

    private static OccupancyChange Add(int slot) => new()
    {
        Kind = OccupancyChangeKind.Add,
        SlotIndex = slot,
        EntryId = Guid.NewGuid(),
        Status = PoleAttachmentOccupantStatus.AuthorizedContracted,
        OccupantOrganizationId = TestIdentities.OtherOrganization,
        OperatorCnpj = "98.765.432/0001-10",
        CableDiameterMm = 9.5m,
        CableLoadDaN = 80m,
    };

    /// <summary>The whole flow, as the Web's clients run it; returns what it observed of the order along the way.</summary>
    private static async Task<Observed> RunSharedUseFlowAsync(Stack stack)
    {
        var provider = stack.Clients;
        var orders = provider.GetRequiredService<IServiceOrderRepository>();
        var selection = provider.GetRequiredService<IOrderTargetSelectionService>();
        var targets = provider.GetRequiredService<IServiceOrderTargetReader>();
        var dispatch = provider.GetRequiredService<ISharedUseDispatchService>();
        var staging = provider.GetRequiredService<IOrderOccupancyStagingService>();
        var ct = CancellationToken.None;

        // create
        await orders.AddAsync(new ServiceOrder
        {
            Id = OrderId,
            Title = "Shared-use pole work",
            OrderTypeId = SharedUseOrderTypes.PoleWorkId,
            Status = ServiceOrderStatus.Draft,
            CreatedBy = "owner-admin",
            OrganizationId = TestIdentities.OwnerOrganization,
            CreatedAt = Now,
        });
        var created = await orders.GetByIdAsync(OrderId, ct);

        // target
        var request = new TargetSelectionRequest(
            OrderId, "identifier-list", new Dictionary<string, object>(StringComparer.Ordinal) { ["featureIds"] = new[] { "pole-1", "pole-2" } });
        var preview = await selection.PreviewAsync(request, Principal, ct);
        var applied = await selection.ApplyAsync(request, Principal, ct);
        var targetCount = await targets.CountAsync(OrderId, ct);

        // The occupancy the dispatch fans out by: seeded in the vertical's own tables, as its own tests do.
        await OccupancySeed.OccupyAsync(stack.Host.ConnectionString, TestIdentities.OwnerOrganization, "pole-1", TestIdentities.OccupantOne);
        await OccupancySeed.OccupyAsync(stack.Host.ConnectionString, TestIdentities.OwnerOrganization, "pole-2", TestIdentities.OccupantOne, TestIdentities.OccupantTwo);

        // dispatch
        var beforeDispatch = await orders.GetByIdAsync(OrderId, ct);
        var dispatched = await dispatch.DispatchAsync(OrderId, beforeDispatch!.RowVersion, Principal, ct);
        var children = await orders.GetChildrenAsync(OrderId, ct);
        var afterDispatch = await orders.GetByIdAsync(OrderId, ct);

        // work, then the occupancy it changes is staged on a pole
        await orders.AppendActionAsync(OrderId, new OrderActionLog(OrderAction.Execute, "owner-admin", Now, ResultingStatus: ServiceOrderStatus.InProgress), ct);
        await orders.AppendActionAsync(OrderId, new OrderActionLog(OrderAction.Complete, "owner-admin", Now, ResultingStatus: ServiceOrderStatus.Completed), ct);
        await staging.StageAsync(OrderId, TargetLedger.TargetId("neutral-operator", OrderId, "pole-1"), [Add(5)], Principal, ct);

        // approve: committed in the host, then waiting for the kernel's apply
        await orders.AppendActionAsync(OrderId, new OrderActionLog(OrderAction.Approve, "owner-admin", Now), ct);
        var awaiting = await orders.GetByIdAsync(OrderId, ct);

        // The kernel's apply, answered as the broker delivers it to the host's saga handler.
        await using (var scope = stack.Host.Services.CreateAsyncScope())
        {
            var handler = scope.ServiceProvider.GetRequiredService<IIntegrationEventHandler<AssetBatchAppliedEvent>>();
            var reply = AssetBatchAppliedEvent.Create(
                OccupancyCommitService.AssetMutationBatchId(OrderId, attempt: 1), "default", TestIdentities.OwnerOrganization, "geoassets-server", "kernel",
                [new AssetBatchAppliedItem("pole-1", AssetMutationKind.UpdateAttributes, "pole", [1])], TimeProvider.System,
                sourceOrderId: OrderId, sourceOrderTypeId: SharedUseOrderTypes.PoleWorkId, sourceVerticalId: OccupancyApplySagaService.VerticalId);
            await handler.HandleAsync(
                reply,
                new IntegrationMessageContext(
                    Guid.NewGuid(), IntegrationEventTypes.AssetMutationBatchAppliedV1, 1, OrderId, "application/x-protobuf", "in-process", "kernel", "occupancy-apply-applied",
                    new DateTimeOffset(Now), new DateTimeOffset(Now)),
                ct);
        }

        var approved = await orders.GetByIdAsync(OrderId, ct);
        return new Observed(created, preview, applied, targetCount, dispatched, children, afterDispatch, awaiting, approved);
    }

    private sealed record Observed(
        IServiceOrder? Created, TargetSelectionPreview Preview, TargetSelectionResult Applied, int TargetCount, SharedUseDispatchResult Dispatched,
        IReadOnlyList<IServiceOrder> Children, IServiceOrder? AfterDispatch, IServiceOrder? Awaiting, IServiceOrder? Approved);

    private static void AssertTheFlowWorked(Observed seen)
    {
        seen.Created!.OrderTypeId.Should().Be(SharedUseOrderTypes.PoleWorkId);
        seen.Created.Status.Should().Be(ServiceOrderStatus.Draft);
        seen.Preview.Eligible.Should().Be(2);
        seen.Applied.Added.Should().Be(2);
        seen.TargetCount.Should().Be(2);
        seen.Dispatched.ChildOrdersCreated.Should().Be(2, "one notice per partner organization");
        seen.Children.Should().HaveCount(2).And.OnlyContain(child => string.Equals(child.OrderTypeId, SharedUseOrderTypes.OccupantNoticeId, StringComparison.Ordinal));
        seen.AfterDispatch!.Status.Should().Be(ServiceOrderStatus.Pending);
        seen.Awaiting!.Status.Should().Be(ServiceOrderStatus.Completed, "the approval waits for the kernel's apply");
        OccupancyApplySagaAttributes.IsAwaitingApply(seen.Awaiting.Attributes).Should().BeTrue();
        seen.Approved!.Status.Should().Be(SharedUseOrderTypes.ApprovedStateKey, "the saga completed on the kernel's answer");
        seen.Approved.Attributes.Should().Contain(OccupancyApplySagaAttributes.StateKey, OccupancyApplySagaAttributes.Applied);
    }

    [Fact]
    public async Task TheWebCreatesTargetsDispatchesAndApprovesASharedUseOrder_ThroughTheGateway_AgainstTheNeutralOperatorHost()
    {
        var stack = await StartAsync(rollback: false);

        var seen = await RunSharedUseFlowAsync(stack);

        AssertTheFlowWorked(seen);
    }

    [Fact]
    public async Task EveryCallOfTheVertical_ReachedTheNeutralOperatorHost_AsApiWorkflow_AndNoneTheKernelsWorkflowRoutes()
    {
        var stack = await StartAsync(rollback: false);

        await RunSharedUseFlowAsync(stack);

        var fromWeb = stack.Web.Requests.Select(r => r.Path).ToList();
        fromWeb.Should().OnlyContain(
            path => path.StartsWith(VerticalRoot, StringComparison.Ordinal) || string.Equals(path, "/api/registry/order-types", StringComparison.Ordinal),
            "the Web talks to the gateway's vertical route, and reads the kernel's registry through it");
        fromWeb.Count(path => path.StartsWith(VerticalRoot, StringComparison.Ordinal)).Should().BeGreaterThan(10);

        stack.Kernel.Paths.Should().Equal(["/api/registry/order-types"], "the kernel's workflow routes were never asked");
        stack.NeutralOperator.Paths.Should().NotBeEmpty().And.OnlyContain(path => path.StartsWith("/api/workflow/", StringComparison.Ordinal));
        stack.NeutralOperator.Paths.Should().HaveCount(fromWeb.Count(path => path.StartsWith(VerticalRoot, StringComparison.Ordinal)), "every vertical call arrived, rewritten");
        stack.NeutralOperator.Requests.Select(r => (r.Method, r.Path)).Should().Contain(
            [
                (HttpMethod.Post, "/api/workflow/service-orders"),
                (HttpMethod.Post, $"/api/workflow/service-orders/{OrderId}/targets/selection/preview"),
                (HttpMethod.Post, $"/api/workflow/service-orders/{OrderId}/targets/selection/apply"),
                (HttpMethod.Post, $"/api/workflow/service-orders/{OrderId}/shared-use/dispatch"),
                (HttpMethod.Post, $"/api/workflow/service-orders/{OrderId}/actions"),
            ]);
    }

    [Fact]
    public async Task TheKernelsRegistry_IsReadOnceThroughTheGateway_AndEveryOrderIsFoundWithoutProbing()
    {
        var stack = await StartAsync(rollback: false);

        await RunSharedUseFlowAsync(stack);

        stack.Web.Requests.Count(r => string.Equals(r.Path, "/api/registry/order-types", StringComparison.Ordinal)).Should().Be(1);
        stack.Web.Paths.Should().NotContain(path => path.StartsWith("/api/workflow/", StringComparison.Ordinal), "an order created at a vertical route is remembered there");
    }

    [Fact]
    public async Task TheWebCreatesTargetsDispatchesAndApprovesASharedUseOrder_ThroughTheGateway_InTheRollbackConfiguration()
    {
        var stack = await StartAsync(rollback: true);

        var seen = await RunSharedUseFlowAsync(stack);

        AssertTheFlowWorked(seen);
    }

    [Fact]
    public async Task InTheRollbackConfiguration_TheVerticalRouteIsRewrittenToTheKernelsWorkflowApi_AndNothingReachesAVerticalHost()
    {
        var stack = await StartAsync(rollback: true);

        await RunSharedUseFlowAsync(stack);

        stack.Web.Paths.Where(path => !string.Equals(path, "/api/registry/order-types", StringComparison.Ordinal))
            .Should().OnlyContain(path => path.StartsWith(VerticalRoot, StringComparison.Ordinal), "the Web's calls are the same in both configurations");
        stack.Kernel.Paths.Should().Contain("/api/registry/order-types");
        stack.Kernel.Paths.Where(path => !string.Equals(path, "/api/registry/order-types", StringComparison.Ordinal))
            .Should().NotBeEmpty().And.OnlyContain(path => path.StartsWith("/api/workflow/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ACallerWithoutAToken_IsRefusedByTheVerticalsHost_BehindTheGateway()
    {
        var stack = await StartAsync(rollback: false);
        using var anonymous = new HttpClient(stack.GatewayHandler) { BaseAddress = new Uri("http://gateway.test/") };

        var response = await anonymous.GetAsync(new Uri($"api/v/neutral-operator/workflow/service-orders/{OrderId}", UriKind.Relative));

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized, "the gateway authorizes nothing: the host decides");
    }
}
