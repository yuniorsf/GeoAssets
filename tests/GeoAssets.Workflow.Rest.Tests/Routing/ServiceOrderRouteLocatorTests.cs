using FluentAssertions;
using GeoAssets.Workflow.Rest.Routing;
using Xunit;

namespace GeoAssets.Workflow.Rest.Tests.Routing;

/// <summary>XD01-384: which root serves an order the client knows only by id.</summary>
public sealed class ServiceOrderRouteLocatorTests
{
    private static string Get(GatewayDouble gateway, int index) => gateway.Requests[index].Uri.AbsoluteUri;

    [Fact]
    public async Task LocateAsync_ForARememberedOrder_IsTheRememberedRoot_WithoutAnyRequest()
    {
        var gateway = new GatewayDouble();
        var sut = new RoutedClients(StubRoutes.WithVertical(), gateway).Locator;
        sut.Remember("o1", GatewayDouble.Vertical);

        (await sut.LocateAsync("o1", default)).Should().Be(GatewayDouble.Vertical);

        gateway.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task LocateAsync_WithASingleRoot_IsThatRoot_WithoutAnyRequest()
    {
        var gateway = new GatewayDouble();
        var sut = new RoutedClients(StubRoutes.KernelOnly(), gateway).Locator;

        (await sut.LocateAsync("o1", default)).Should().Be(GatewayDouble.Kernel);

        gateway.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task LocateAsync_ForAnUnknownOrder_ProbesTheRootsInOrder_KernelFirst_AndRemembersTheFirstThatReturnsIt()
    {
        var gateway = new GatewayDouble().Serves(GatewayDouble.KernelPath).Serves(GatewayDouble.VerticalPath, GatewayDouble.Order("o1"));
        var sut = new RoutedClients(StubRoutes.WithVertical(), gateway).Locator;

        (await sut.LocateAsync("o1", default)).Should().Be(GatewayDouble.Vertical);
        (await sut.LocateAsync("o1", default)).Should().Be(GatewayDouble.Vertical);

        gateway.Requests.Should().HaveCount(2, "the kernel, then the vertical; the second location is remembered");
        Get(gateway, 0).Should().Be("http://gw/api/workflow/service-orders/o1");
        Get(gateway, 1).Should().Be("http://gw/api/v/vertical-a/workflow/service-orders/o1");
    }

    [Fact]
    public async Task LocateAsync_WhenTheKernelHasTheOrder_StopsThere()
    {
        var gateway = new GatewayDouble().Serves(GatewayDouble.KernelPath, GatewayDouble.Order("o1")).Serves(GatewayDouble.VerticalPath, GatewayDouble.Order("o1"));
        var sut = new RoutedClients(StubRoutes.WithVertical(), gateway).Locator;

        (await sut.LocateAsync("o1", default)).Should().Be(GatewayDouble.Kernel);

        gateway.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task FindAsync_AfterProbing_HandsBackTheOrderItRead_AndNothingWhenItWasRemembered()
    {
        var gateway = new GatewayDouble().Serves(GatewayDouble.KernelPath).Serves(GatewayDouble.VerticalPath, GatewayDouble.Order("o1"));
        var sut = new RoutedClients(StubRoutes.WithVertical(), gateway).Locator;

        var (_, probed) = await sut.FindAsync("o1", default);
        var (_, remembered) = await sut.FindAsync("o1", default);

        probed!.Id.Should().Be("o1");
        remembered.Should().BeNull();
    }

    [Fact]
    public async Task LocateAsync_ForAnOrderNoRootReturns_FallsBackToTheKernel_SoItAnswersAs404_AndRemembersNothing()
    {
        var gateway = new GatewayDouble().Serves(GatewayDouble.KernelPath).Serves(GatewayDouble.VerticalPath);
        var sut = new RoutedClients(StubRoutes.WithVertical(), gateway).Locator;

        (await sut.LocateAsync("ghost", default)).Should().Be(GatewayDouble.Kernel);
        await sut.LocateAsync("ghost", default);

        gateway.Requests.Should().HaveCount(4, "both roots are asked again: a miss is not remembered");
    }

    [Fact]
    public Task LocateAsync_WhenARootFails_TheFailurePropagates()
    {
        var gateway = new GatewayDouble().Serves(GatewayDouble.KernelPath).Fails(GatewayDouble.VerticalPath);
        var sut = new RoutedClients(StubRoutes.WithVertical(), gateway).Locator;

        var act = () => sut.LocateAsync("o1", default);

        return act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task Forget_MakesTheOrderBeLocatedAgain()
    {
        var gateway = new GatewayDouble().Serves(GatewayDouble.KernelPath, GatewayDouble.Order("o1"));
        var sut = new RoutedClients(StubRoutes.WithVertical(), gateway).Locator;
        sut.Remember("o1", GatewayDouble.Vertical);

        sut.Forget("o1");

        (await sut.LocateAsync("o1", default)).Should().Be(GatewayDouble.Kernel);
        gateway.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task Remember_AtTheMemoryCap_StartsOver_ForANewOrder_ButNotForAKnownOne()
    {
        var gateway = new GatewayDouble().Serves(GatewayDouble.KernelPath, GatewayDouble.Order("o0"), GatewayDouble.Order("o1"));
        var sut = new RoutedClients(StubRoutes.WithVertical(), gateway).Locator;
        for (var i = 0; i < ServiceOrderRouteLocator.MaxRemembered; i++) sut.Remember($"o{i}", GatewayDouble.Vertical);

        sut.Remember("o1", GatewayDouble.Vertical);
        (await sut.LocateAsync("o0", default)).Should().Be(GatewayDouble.Vertical, "re-remembering a known order does not evict anything");
        gateway.Requests.Should().BeEmpty();

        sut.Remember("overflow", GatewayDouble.Vertical);

        (await sut.LocateAsync("overflow", default)).Should().Be(GatewayDouble.Vertical);
        gateway.Requests.Should().BeEmpty("the newest order is remembered");
        (await sut.LocateAsync("o0", default)).Should().Be(GatewayDouble.Kernel, "the memory started over: o0 is found again, on the kernel");
        gateway.Requests.Should().ContainSingle();
    }

    [Fact]
    public void MaxRemembered_IsTenThousand() => ServiceOrderRouteLocator.MaxRemembered.Should().Be(10_000);

    [Fact]
    public Task LocateAsync_WithABlankOrderId_IsRejected()
    {
        var sut = new RoutedClients(StubRoutes.KernelOnly(), new GatewayDouble()).Locator;

        var act = () => sut.LocateAsync(" ", default);

        return act.Should().ThrowAsync<ArgumentException>();
    }
}
