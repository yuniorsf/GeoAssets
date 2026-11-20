using FluentAssertions;
using GeoAssets.Core.Models;
using GeoAssets.NeutralOperator.Rest.Routing;
using GeoAssets.Workflow.Rest.Routing;
using GeoAssets.Workflow.Rules;
using GeoAssets.Workflow.Targets;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GeoAssets.Workflow.Rest.Tests.Routing;

/// <summary>XD01-384: targets, selection, occupancy and dispatch reach the host that serves the order; the order types and the map selection are routed by their own rule.</summary>
public sealed class DescriptorRoutedPortsTests
{
    private static readonly WorkflowPrincipal _principal = new("u", Guid.NewGuid().ToString(), [], [], []);

    private static (RoutedClients Clients, GatewayDouble Gateway) Create(Uri servingRoot)
    {
        var gateway = new GatewayDouble();
        var clients = new RoutedClients(StubRoutes.WithVertical("shared-use-pole-work"), gateway);
        clients.Locator.Remember("o1", servingRoot);
        return (clients, gateway);
    }

    private static string RootPath(bool onVertical) => onVertical ? GatewayDouble.VerticalPath : GatewayDouble.KernelPath;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Targets_AreCountedAtTheRootServingTheOrder(bool onVertical)
    {
        var (clients, gateway) = Create(onVertical ? GatewayDouble.Vertical : GatewayDouble.Kernel);
        var sut = new DescriptorRoutedServiceOrderTargets(clients.Targets, clients.Locator);

        var act = () => sut.CountAsync("o1", default);

        await act.Should().ThrowAsync<Exception>("the double answers an empty body");
        gateway.Requests.Single().Uri.AbsolutePath.Should().Be($"{RootPath(onVertical)}service-orders/o1/targets/count");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ATarget_IsReadAtTheRootServingTheOrder(bool onVertical)
    {
        var (clients, gateway) = Create(onVertical ? GatewayDouble.Vertical : GatewayDouble.Kernel);
        var sut = new DescriptorRoutedServiceOrderTargets(clients.Targets, clients.Locator);

        await sut.GetByIdAsync("o1", "t1", default);

        gateway.Requests.Single().Uri.AbsolutePath.Should().Be($"{RootPath(onVertical)}service-orders/o1/targets/t1");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SelectionPreview_IsPostedAtTheRootServingTheOrder(bool onVertical)
    {
        var (clients, gateway) = Create(onVertical ? GatewayDouble.Vertical : GatewayDouble.Kernel);
        var sut = new DescriptorRoutedOrderTargetSelectionService(clients.Selections, clients.Locator);

        var act = () => sut.PreviewAsync(new TargetSelectionRequest("o1", "identifier-list", new Dictionary<string, object>(StringComparer.Ordinal)), _principal, default);

        await act.Should().ThrowAsync<Exception>("the double answers an empty body");
        var request = gateway.Requests.Single();
        request.Method.Should().Be(HttpMethod.Post);
        request.Uri.AbsolutePath.Should().Be($"{RootPath(onVertical)}service-orders/o1/targets/selection/preview");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OccupancyChanges_AreDiscardedAtTheRootServingTheOrder(bool onVertical)
    {
        var (clients, gateway) = Create(onVertical ? GatewayDouble.Vertical : GatewayDouble.Kernel);
        var sut = new DescriptorRoutedOrderOccupancyStagingService(clients.Occupancy, clients.Locator);

        var act = () => sut.DiscardAllAsync("o1", _principal, default);

        await act.Should().ThrowAsync<Exception>("the double answers an empty body");
        var request = gateway.Requests.Single();
        request.Method.Should().Be(HttpMethod.Delete);
        request.Uri.AbsolutePath.Should().Be($"{RootPath(onVertical)}service-orders/o1/occupancy-changes");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TheDispatch_IsPostedAtTheRootServingTheOrder(bool onVertical)
    {
        var (clients, gateway) = Create(onVertical ? GatewayDouble.Vertical : GatewayDouble.Kernel);
        var sut = new DescriptorRoutedSharedUseDispatchClient(clients.Dispatch, clients.Locator);

        var act = () => sut.DispatchAsync("o1", [1, 2, 3], _principal, default);

        await act.Should().ThrowAsync<Exception>("the double answers an empty body");
        var request = gateway.Requests.Single();
        request.Method.Should().Be(HttpMethod.Post);
        request.Uri.AbsolutePath.Should().Be($"{RootPath(onVertical)}service-orders/o1/shared-use/dispatch");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TheDispatchPlan_IsReadAtTheRootServingTheOrder(bool onVertical)
    {
        var (clients, gateway) = Create(onVertical ? GatewayDouble.Vertical : GatewayDouble.Kernel);
        var sut = new DescriptorRoutedSharedUseDispatchClient(clients.Dispatch, clients.Locator);

        var act = () => sut.GetUnnotifiablePolesAsync("o1", 1, 10, _principal, default);

        await act.Should().ThrowAsync<Exception>("the double answers an empty body");
        gateway.Requests.Single().Uri.PathAndQuery.Should().Be($"{RootPath(onVertical)}service-orders/o1/shared-use/dispatch-plan/unnotifiable-poles?page=1&pageSize=10");
    }

    [Fact]
    public async Task AnOrderNobodyKnows_IsProbedOnce_ThenItsOperationGoesToTheKernel()
    {
        var gateway = new GatewayDouble().Serves(GatewayDouble.KernelPath).Serves(GatewayDouble.VerticalPath);
        var clients = new RoutedClients(StubRoutes.WithVertical(), gateway);
        var sut = new DescriptorRoutedServiceOrderTargets(clients.Targets, clients.Locator);

        await sut.GetByIdAsync("ghost", "t1", default);

        gateway.PathsOf(HttpMethod.Get).Should().Equal(
            "/api/workflow/service-orders/ghost", "/api/v/vertical-a/workflow/service-orders/ghost", "/api/workflow/service-orders/ghost/targets/t1");
    }

    [Fact]
    public Task TargetOperations_WithABlankOrderId_AreRejected()
    {
        var (clients, _) = Create(GatewayDouble.Kernel);
        var sut = new DescriptorRoutedServiceOrderTargets(clients.Targets, clients.Locator);

        var act = () => sut.GetByIdAsync(" ", "t1", default);

        return act.Should().ThrowAsync<ArgumentException>();
    }

    // ── Order types ───────────────────────────────────────────────────────────

    [Fact]
    public async Task AnOrderType_IsReadFromTheRootItsDescriptorNames()
    {
        var gateway = new GatewayDouble();
        var sut = new DescriptorRoutedOrderTypeRepository(StubRoutes.WithVertical("shared-use-pole-work"), new RoutedClients(StubRoutes.WithVertical("shared-use-pole-work"), gateway).OrderTypes);

        await sut.GetByIdAsync("shared-use-pole-work");
        await sut.GetByIdAsync("inspection");

        gateway.PathsOf(HttpMethod.Get).Should().Equal(
            "/api/v/vertical-a/workflow/order-types/shared-use-pole-work", "/api/workflow/order-types/inspection");
    }

    [Fact]
    public async Task OrderTypeWrites_GoToTheRootItsDescriptorNames()
    {
        var gateway = new GatewayDouble();
        var routes = StubRoutes.WithVertical("shared-use-pole-work");
        var sut = new DescriptorRoutedOrderTypeRepository(routes, new RoutedClients(routes, gateway).OrderTypes);
        var type = new GeoAssets.Workflow.Orders.OrderType { Id = "shared-use-pole-work", DisplayName = "Pole work" };

        await sut.AddAsync(type);
        await sut.UpdateAsync(type);
        await sut.DeleteAsync("shared-use-pole-work");
        await sut.SaveChangesAsync();

        gateway.Requests.Select(r => (r.Method, r.Uri.AbsolutePath)).Should().Equal(
            (HttpMethod.Post, "/api/v/vertical-a/workflow/order-types"),
            (HttpMethod.Put, "/api/v/vertical-a/workflow/order-types/shared-use-pole-work"),
            (HttpMethod.Delete, "/api/v/vertical-a/workflow/order-types/shared-use-pole-work"));
    }

    [Fact]
    public async Task GetAllAsync_MergesEachRootsTypesOnce_AndAsksEachRootOnce()
    {
        var gateway = new GatewayDouble().Serves(GatewayDouble.KernelPath).Serves(GatewayDouble.VerticalPath);
        var routes = StubRoutes.WithVertical("shared-use-pole-work");
        var sut = new DescriptorRoutedOrderTypeRepository(routes, new RoutedClients(routes, gateway).OrderTypes);

        await sut.GetAllAsync();

        gateway.PathsOf(HttpMethod.Get).Should().Equal("/api/workflow/order-types", "/api/v/vertical-a/workflow/order-types");
    }

    [Fact]
    public async Task GetAllAsync_WithASingleRoot_IsThatHostsAnswer()
    {
        var gateway = new GatewayDouble();
        var routes = StubRoutes.KernelOnly();
        var sut = new DescriptorRoutedOrderTypeRepository(routes, new RoutedClients(routes, gateway).OrderTypes);

        await sut.GetAllAsync();

        gateway.PathsOf(HttpMethod.Get).Should().Equal("/api/workflow/order-types");
    }

    // ── Registration ──────────────────────────────────────────────────────────

    [Fact]
    public async Task TheMapSelectionResolve_StaysOnTheKernelsRoot()
    {
        var gateway = new GatewayDouble();
        var services = new ServiceCollection();
        services.AddHttpClient("GeoAssetsServer").ConfigurePrimaryHttpMessageHandler(() => gateway);
        services.AddDescriptorRoutedWorkflowRest("http://gw/api/workflow");
        var client = services.BuildServiceProvider().GetRequiredService<RestMapSelectionClient>();

        var act = () => client.ResolveAsync("identifier-list", new Dictionary<string, object>(StringComparer.Ordinal), default);

        await act.Should().ThrowAsync<Exception>("the double answers an empty body");
        var request = gateway.Requests.Single();
        request.Method.Should().Be(HttpMethod.Post);
        request.Uri.Should().Be(new Uri("http://gw/api/workflow/selection/resolve"));
    }
}
