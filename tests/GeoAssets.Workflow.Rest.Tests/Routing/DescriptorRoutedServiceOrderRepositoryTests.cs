using FluentAssertions;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rest.Routing;
using Xunit;

namespace GeoAssets.Workflow.Rest.Tests.Routing;

/// <summary>XD01-384: the service-order port over the kernel's workflow API and the verticals' that the gateway fronts.</summary>
public sealed class DescriptorRoutedServiceOrderRepositoryTests
{
    private const string Vertical = "shared-use-pole-work";

    private static (DescriptorRoutedServiceOrderRepository Sut, GatewayDouble Gateway, ServiceOrderRouteLocator Locator) Create(StubRoutes routes, GatewayDouble gateway)
    {
        var clients = new RoutedClients(routes, gateway);
        return (clients.Orders, gateway, clients.Locator);
    }

    private static GatewayDouble TwoRoots() => new GatewayDouble()
        .Serves(GatewayDouble.KernelPath, GatewayDouble.Order("k1"), GatewayDouble.Order("k2"))
        .Serves(GatewayDouble.VerticalPath, GatewayDouble.Order("v1", Vertical), GatewayDouble.Order("v2", Vertical));

    // ── Create / update by order type ─────────────────────────────────────────

    [Fact]
    public async Task AddAsync_GoesToTheRootOfTheOrdersOwnType_AndRemembersIt()
    {
        var (sut, gateway, locator) = Create(StubRoutes.WithVertical(Vertical), TwoRoots());

        await sut.AddAsync(GatewayDouble.Order("n1", Vertical));

        gateway.PathsOf(HttpMethod.Post).Should().Equal("/api/v/vertical-a/workflow/service-orders");
        (await locator.LocateAsync("n1", default)).Should().Be(GatewayDouble.Vertical);
        gateway.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task AddAsync_OfATypeNoDescriptorNames_GoesToTheKernel()
    {
        var (sut, gateway, _) = Create(StubRoutes.WithVertical(Vertical), TwoRoots());

        await sut.AddAsync(GatewayDouble.Order("n1", "inspection"));

        gateway.PathsOf(HttpMethod.Post).Should().Equal("/api/workflow/service-orders");
    }

    [Fact]
    public async Task UpdateAsync_GoesToTheRootOfTheOrdersOwnType_WithoutLocatingIt()
    {
        var (sut, gateway, _) = Create(StubRoutes.WithVertical(Vertical), TwoRoots());

        await sut.UpdateAsync(GatewayDouble.Order("v1", Vertical));

        gateway.PathsOf(HttpMethod.Put).Should().Equal("/api/v/vertical-a/workflow/service-orders/v1");
        gateway.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task AddAsync_RaisesTheEventOnce_AndAFailedAddRaisesNone()
    {
        var added = new List<string>();
        var (sut, _, _) = Create(StubRoutes.WithVertical(Vertical), TwoRoots());
        sut.OrderAdded += (_, order) => added.Add(order.Id);

        await sut.AddAsync(GatewayDouble.Order("n1", Vertical));
        await sut.AddAsync(GatewayDouble.Order("n2", Vertical));

        added.Should().Equal("n1", "n2");

        var (failing, _, _) = Create(StubRoutes.WithVertical(Vertical), new GatewayDouble().Fails(GatewayDouble.VerticalPath));
        failing.OrderAdded += (_, order) => added.Add(order.Id);
        var act = async () => await failing.AddAsync(GatewayDouble.Order("n3", Vertical));
        await act.Should().ThrowAsync<HttpRequestException>();
        added.Should().Equal("n1", "n2");
    }

    // ── By id ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetByIdAsync_FindsAnOrderOfTheVertical_WithOneReadPerRootProbed_AndNoSecondRead()
    {
        var (sut, gateway, _) = Create(StubRoutes.WithVertical(Vertical), TwoRoots());

        var order = await sut.GetByIdAsync("v2");

        order!.Id.Should().Be("v2");
        gateway.PathsOf(HttpMethod.Get).Should().Equal("/api/workflow/service-orders/v2", "/api/v/vertical-a/workflow/service-orders/v2");
    }

    [Fact]
    public async Task GetByIdAsync_OfAnOrderNoRootReturns_IsNull_AnsweredByTheKernel()
    {
        var (sut, gateway, _) = Create(StubRoutes.WithVertical(Vertical), TwoRoots());

        (await sut.GetByIdAsync("ghost")).Should().BeNull();

        gateway.PathsOf(HttpMethod.Get).Last().Should().Be("/api/workflow/service-orders/ghost");
    }

    [Theory]
    [InlineData("children", "/api/v/vertical-a/workflow/service-orders/v1/children")]
    [InlineData("parent", "/api/v/vertical-a/workflow/service-orders/v1/parent")]
    public async Task ChildrenAndParent_AreReadFromTheRootServingTheOrder(string which, string expectedPath)
    {
        var (sut, gateway, locator) = Create(StubRoutes.WithVertical(Vertical), TwoRoots());
        locator.Remember("v1", GatewayDouble.Vertical);

        if (string.Equals(which, "children", StringComparison.Ordinal)) await sut.GetChildrenAsync("v1");
        else await sut.GetParentAsync("v1");

        gateway.PathsOf(HttpMethod.Get).Should().Equal(expectedPath);
    }

    [Fact]
    public async Task DispatchActionAndDelete_GoToTheRootServingTheOrder()
    {
        var (sut, gateway, locator) = Create(StubRoutes.WithVertical(Vertical), TwoRoots());
        locator.Remember("v1", GatewayDouble.Vertical);

        await sut.DeleteAsync("v1");

        gateway.PathsOf(HttpMethod.Delete).Should().Equal("/api/v/vertical-a/workflow/service-orders/v1");
        (await sut.GetByIdAsync("v1")).Should().BeNull("a deleted order is forgotten and located again: no root returns it");
    }

    // ── Lists ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetByOrderTypeAsync_AsksThatTypesRootOnly()
    {
        var (sut, gateway, _) = Create(StubRoutes.WithVertical(Vertical), TwoRoots());

        var orders = await sut.GetByOrderTypeAsync(Vertical);

        orders.Select(o => o.Id).Should().Equal("v1", "v2");
        gateway.PathsOf(HttpMethod.Get).Should().Equal($"/api/v/vertical-a/workflow/service-orders/by-order-type/{Vertical}");
    }

    [Fact]
    public async Task GetByOrderTypeAsync_OfAKernelType_AsksTheKernelOnly()
    {
        var (sut, gateway, _) = Create(StubRoutes.WithVertical(Vertical), TwoRoots());

        await sut.GetByOrderTypeAsync("inspection");

        gateway.PathsOf(HttpMethod.Get).Should().Equal("/api/workflow/service-orders/by-order-type/inspection");
    }

    [Fact]
    public async Task GetAllAsync_AsksEachRootOnce_InOrder_AndConcatenatesWithoutReSorting()
    {
        var gateway = new GatewayDouble()
            .Serves(GatewayDouble.KernelPath, GatewayDouble.Order("z"), GatewayDouble.Order("b"))
            .Serves(GatewayDouble.VerticalPath, GatewayDouble.Order("a", Vertical));
        var (sut, _, _) = Create(StubRoutes.WithVertical(Vertical), gateway);

        var orders = await sut.GetAllAsync();

        orders.Select(o => o.Id).Should().Equal("z", "b", "a");
        gateway.PathsOf(HttpMethod.Get).Should().Equal("/api/workflow/service-orders", "/api/v/vertical-a/workflow/service-orders");
    }

    [Fact]
    public async Task GetAllAsync_ListedByTwoRoots_KeepsTheFirstCopyOnly()
    {
        var gateway = new GatewayDouble()
            .Serves(GatewayDouble.KernelPath, GatewayDouble.Order("dup", "inspection"))
            .Serves(GatewayDouble.VerticalPath, GatewayDouble.Order("dup", Vertical), GatewayDouble.Order("v1", Vertical));
        var (sut, _, locator) = Create(StubRoutes.WithVertical(Vertical), gateway);

        var orders = await sut.GetAllAsync();

        orders.Select(o => (o.Id, o.OrderTypeId)).Should().Equal(("dup", "inspection"), ("v1", Vertical));
        (await locator.LocateAsync("dup", default)).Should().Be(GatewayDouble.Kernel);
        (await locator.LocateAsync("v1", default)).Should().Be(GatewayDouble.Vertical);
    }

    [Fact]
    public async Task GetAllAsync_WithASingleRoot_IsThatHostsAnswer_Unchanged()
    {
        var gateway = new GatewayDouble().Serves(GatewayDouble.KernelPath, GatewayDouble.Order("b"), GatewayDouble.Order("a"), GatewayDouble.Order("b"));
        var (sut, _, _) = Create(StubRoutes.KernelOnly(), gateway);

        (await sut.GetAllAsync()).Select(o => o.Id).Should().Equal("b", "a", "b");
    }

    [Theory]
    [InlineData(GatewayDouble.KernelPath)]
    [InlineData(GatewayDouble.VerticalPath)]
    public Task GetAllAsync_WhenAnyRootFails_TheListFails(string failing)
    {
        var gateway = TwoRoots().Fails(failing);
        var (sut, _, _) = Create(StubRoutes.WithVertical(Vertical), gateway);

        var act = () => sut.GetAllAsync();

        return act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task EveryMergedList_AsksEachRootOnce()
    {
        var (sut, gateway, _) = Create(StubRoutes.WithVertical(Vertical), TwoRoots());

        await sut.GetRootsAsync();
        await sut.GetByStatusAsync("Draft");
        await sut.GetByAssigneeAsync("u");
        await sut.GetByCreatorAsync("u");
        await sut.GetByDateRangeAsync(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc));
        await sut.GetDispatchedToAsync("t", DispatchTargetType.User);

        gateway.Requests.Should().HaveCount(12);
        gateway.RequestUris.Select(u => u.AbsolutePath).Distinct(StringComparer.Ordinal).Should().OnlyContain(
            p => p.StartsWith("/api/workflow/service-orders/", StringComparison.Ordinal) || p.StartsWith("/api/v/vertical-a/workflow/service-orders/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AListedOrder_IsRemembered_SoALaterReadProbesNothing()
    {
        var (sut, gateway, _) = Create(StubRoutes.WithVertical(Vertical), TwoRoots());
        await sut.GetAllAsync();
        gateway.Requests.Clear();

        await sut.GetByIdAsync("v1");

        gateway.PathsOf(HttpMethod.Get).Should().Equal("/api/v/vertical-a/workflow/service-orders/v1");
    }

    [Fact]
    public async Task TheRootsEvents_AreRelayedAsTheRoutedRepositorys()
    {
        var gateway = TwoRoots();
        var (sut, _, locator) = Create(StubRoutes.WithVertical(Vertical), gateway);
        var deleted = new List<string>();
        sut.OrderDeleted += (_, id) => deleted.Add(id);
        locator.Remember("v1", GatewayDouble.Vertical);

        await sut.DeleteAsync("v1");

        deleted.Should().Equal("v1");
    }

    [Fact]
    public Task AWriteToAFailingRoot_Throws()
    {
        var (sut, _, _) = Create(StubRoutes.WithVertical(Vertical), TwoRoots().Fails(GatewayDouble.VerticalPath));

        var act = () => sut.AddAsync(GatewayDouble.Order("n", Vertical));

        return act.Should().ThrowAsync<Exception>();
    }
}
