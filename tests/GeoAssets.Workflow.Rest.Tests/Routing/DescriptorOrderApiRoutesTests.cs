using System.Net;
using System.Text;
using FluentAssertions;
using GeoAssets.Workflow.Rest.Routing;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GeoAssets.Workflow.Rest.Tests.Routing;

/// <summary>XD01-384: where a client finds the workflow API of an order type — from the kernel's registry, on the gateway's origin.</summary>
public sealed class DescriptorOrderApiRoutesTests
{
    private const string Vertical = "/api/v/vertical-a/workflow/service-orders";
    private static readonly Uri _gateway = new("http://gw/api/workflow");

    private static string Descriptors(params (string OrderType, string Route)[] descriptors) =>
        "[" + string.Join(',', descriptors.Select(d => $$"""{ "orderTypeId": "{{d.OrderType}}", "apiBaseRoute": "{{d.Route}}" }""")) + "]";

    private static HttpResponseMessage Ok(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static (DescriptorOrderApiRoutes Sut, FakeHttpMessageHandler Handler, FakeTimeProvider Clock) Create(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new FakeHttpMessageHandler(respond);
        var clock = new FakeTimeProvider();
        return (new DescriptorOrderApiRoutes(new HttpClient(handler), _gateway, clock, DescriptorOrderApiRoutes.DefaultRefreshInterval), handler, clock);
    }

    [Fact]
    public void Constructor_WithARelativeRoot_IsRejected()
    {
        var act = () => new DescriptorOrderApiRoutes(new HttpClient(), new Uri("/api/workflow", UriKind.Relative), TimeProvider.System, TimeSpan.FromMinutes(1));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_WithANonPositiveRefreshInterval_IsRejected()
    {
        var act = () => new DescriptorOrderApiRoutes(new HttpClient(), _gateway, TimeProvider.System, TimeSpan.Zero);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void DefaultRefreshInterval_IsFiveMinutes() =>
        DescriptorOrderApiRoutes.DefaultRefreshInterval.Should().Be(TimeSpan.FromMinutes(5));

    [Fact]
    public void DefaultRoot_GetsATrailingSlash()
    {
        var (sut, _, _) = Create(_ => Ok("[]"));

        sut.DefaultRoot.Should().Be(new Uri("http://gw/api/workflow/"));
    }

    [Theory]
    [InlineData("/api/v/vertical-a/workflow/service-orders", "http://gw/api/v/vertical-a/workflow/")]
    [InlineData("/api/v/vertical-a/workflow/service-orders/", "http://gw/api/v/vertical-a/workflow/")]
    [InlineData("/api/workflow/service-orders", "http://gw/api/workflow/")]
    [InlineData("/api/v/vertical-a/orders", "http://gw/api/v/vertical-a/orders/")]
    public void RootOf_TakesTheParentOfTheServiceOrdersCollection_OtherwiseTheRouteItself(string apiBaseRoute, string expected)
    {
        var (sut, _, _) = Create(_ => Ok("[]"));

        sut.RootOf(apiBaseRoute).Should().Be(new Uri(expected));
    }

    [Fact]
    public async Task ResolveAsync_ForADescribedOrderType_IsThatDescriptorsRoot_OnTheGatewaysOrigin()
    {
        var (sut, _, _) = Create(_ => Ok(Descriptors(("pole-work", Vertical))));

        (await sut.ResolveAsync("pole-work", default)).Should().Be(new Uri("http://gw/api/v/vertical-a/workflow/"));
    }

    [Fact]
    public async Task ResolveAsync_ForAnUnknownOrderType_IsTheKernelsRoot()
    {
        var (sut, _, _) = Create(_ => Ok(Descriptors(("pole-work", Vertical))));

        (await sut.ResolveAsync("inspection", default)).Should().Be(sut.DefaultRoot);
    }

    [Fact]
    public async Task ResolveAsync_ForADescriptorOnTheKernelsOwnRoute_IsTheKernelsRoot()
    {
        var (sut, _, _) = Create(_ => Ok(Descriptors(("inspection", "/api/workflow/service-orders"))));

        (await sut.ResolveAsync("inspection", default)).Should().Be(sut.DefaultRoot);
        (await sut.GetRootsAsync(default)).Should().Equal(sut.DefaultRoot);
    }

    [Fact]
    public async Task GetRootsAsync_ListsTheKernelFirst_ThenTheVerticalRootsInOrdinalOrder_EachOnce()
    {
        var (sut, _, _) = Create(_ => Ok(Descriptors(
            ("t-1", "/api/v/zeta/workflow/service-orders"),
            ("t-2", "/api/v/alpha/workflow/service-orders"),
            ("t-3", "/api/v/zeta/workflow/service-orders"),
            ("t-4", "/api/workflow/service-orders"))));

        (await sut.GetRootsAsync(default)).Should().Equal(
            sut.DefaultRoot, new Uri("http://gw/api/v/alpha/workflow/"), new Uri("http://gw/api/v/zeta/workflow/"));
    }

    [Fact]
    public async Task GetRootsAsync_IgnoresDescriptorsWithoutAnOrderTypeOrARoute()
    {
        var (sut, _, _) = Create(_ => Ok("""[ { "orderTypeId": "", "apiBaseRoute": "/api/v/x/workflow/service-orders" }, { "orderTypeId": "t", "apiBaseRoute": " " }, { } ]"""));

        (await sut.GetRootsAsync(default)).Should().Equal(sut.DefaultRoot);
    }

    [Fact]
    public async Task TheRegistry_IsReadAtTheKernelsRegistryRoute_OnTheGatewaysOrigin_Once_ForRepeatedCalls()
    {
        var (sut, handler, _) = Create(_ => Ok(Descriptors(("pole-work", Vertical))));

        await sut.GetRootsAsync(default);
        await sut.ResolveAsync("pole-work", default);
        await sut.ResolveAsync("inspection", default);

        handler.Requests.Should().ContainSingle().Which.RequestUri.Should().Be(new Uri("http://gw/api/registry/order-types"));
    }

    [Fact]
    public async Task TheRegistry_IsReadAgain_OnlyOnceFiveMinutesHavePassed()
    {
        var (sut, handler, clock) = Create(_ => Ok(Descriptors(("pole-work", Vertical))));
        await sut.GetRootsAsync(default);

        clock.Advance(TimeSpan.FromMinutes(5) - TimeSpan.FromSeconds(1));
        await sut.GetRootsAsync(default);
        handler.Requests.Should().HaveCount(1, "four minutes fifty-nine seconds is still inside the cache");

        clock.Advance(TimeSpan.FromSeconds(1));
        await sut.GetRootsAsync(default);
        handler.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task ARefresh_PicksUpANewlyPublishedRoute()
    {
        var published = Descriptors();
        var (sut, _, clock) = Create(_ => Ok(published));
        (await sut.ResolveAsync("pole-work", default)).Should().Be(sut.DefaultRoot);

        published = Descriptors(("pole-work", Vertical));
        clock.Advance(TimeSpan.FromMinutes(5));

        (await sut.ResolveAsync("pole-work", default)).Should().Be(new Uri("http://gw/api/v/vertical-a/workflow/"));
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task AFailedRefresh_KeepsTheRoutesAlreadyKnown_AndTheNextCallTriesAgain(HttpStatusCode failure)
    {
        var fails = false;
        var (sut, handler, clock) = Create(_ => fails ? new HttpResponseMessage(failure) : Ok(Descriptors(("pole-work", Vertical))));
        await sut.GetRootsAsync(default);
        clock.Advance(TimeSpan.FromMinutes(5));
        fails = true;

        (await sut.ResolveAsync("pole-work", default)).Should().Be(new Uri("http://gw/api/v/vertical-a/workflow/"));
        (await sut.ResolveAsync("pole-work", default)).Should().Be(new Uri("http://gw/api/v/vertical-a/workflow/"));

        handler.Requests.Should().HaveCount(3, "the first read, the failed refresh, and the retry of the next call");
    }

    [Fact]
    public async Task AFailedRefresh_ThatThrows_KeepsTheRoutesAlreadyKnown()
    {
        var down = false;
        var (sut, _, clock) = Create(_ => down ? throw new HttpRequestException("down") : Ok(Descriptors(("pole-work", Vertical))));
        await sut.GetRootsAsync(default);
        clock.Advance(TimeSpan.FromMinutes(5));
        down = true;

        (await sut.GetRootsAsync(default)).Should().HaveCount(2);
    }

    [Fact]
    public async Task TheFirstReadsFailure_Propagates_AndTheNextCallReadsAgain()
    {
        var fails = true;
        var (sut, handler, _) = Create(_ => fails ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : Ok(Descriptors(("pole-work", Vertical))));

        var first = async () => await sut.GetRootsAsync(default);
        await first.Should().ThrowAsync<HttpRequestException>();
        fails = false;

        (await sut.ResolveAsync("pole-work", default)).Should().Be(new Uri("http://gw/api/v/vertical-a/workflow/"));
        handler.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task AForbiddenRegistry_PutsEveryOrderTypeOnTheKernel()
    {
        var (sut, _, _) = Create(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));

        (await sut.GetRootsAsync(default)).Should().Equal(sut.DefaultRoot);
        (await sut.ResolveAsync("pole-work", default)).Should().Be(sut.DefaultRoot);
    }

    [Fact]
    public async Task ACancelledCaller_IsNotServed()
    {
        var (sut, handler, _) = Create(_ => Ok("[]"));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = async () => await sut.GetRootsAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public Task ResolveAsync_WithABlankOrderType_IsRejected()
    {
        var (sut, _, _) = Create(_ => Ok("[]"));

        var act = () => sut.ResolveAsync(" ", default);

        return act.Should().ThrowAsync<ArgumentException>();
    }
}
