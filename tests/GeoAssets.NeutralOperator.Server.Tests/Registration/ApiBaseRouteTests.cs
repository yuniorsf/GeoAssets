using System.Text.Json;
using FluentAssertions;
using GeoAssets.NeutralOperator.Modules;
using GeoAssets.NeutralOperator.Server.Tests.Support;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Registry;
using Xunit;

namespace GeoAssets.NeutralOperator.Server.Tests.Registration;

/// <summary>
/// XD01-384: the descriptors tell clients to reach the vertical through the gateway's <c>/api/v/neutral-operator/**</c> route,
/// whichever host publishes them — this host over the wire, or the kernel while it still hosts the module (the rollback) —
/// so a client's routes never depend on where the vertical runs.
/// </summary>
public sealed class ApiBaseRouteTests
{
    private const string GatewayRoute = "/api/v/neutral-operator/workflow/service-orders";

    private static IReadOnlyList<OrderTypeDescriptor> Published()
    {
        var registry = new OrderTypeRegistry();
        NeutralOperatorModule.RegisterOrderTypes(registry);
        return NeutralOperatorModule.PublishedDescriptors(registry);
    }

    [Fact]
    public void TheModulesApiBaseRoute_IsTheGatewaysVerticalPrefix_FollowedByTheWorkflowServiceOrdersCollection() =>
        NeutralOperatorModule.ApiBaseRoute.Should().Be(GatewayRoute);

    [Fact]
    public void EveryPublishedDescriptor_NamesTheGatewayRoute()
    {
        var published = Published();

        published.Should().NotBeEmpty();
        published.Should().OnlyContain(d => string.Equals(d.ApiBaseRoute, GatewayRoute, StringComparison.Ordinal));
    }

    [Fact]
    public void EveryFeatureAction_IsReachedUnderTheDescriptorsApiBaseRoute()
    {
        var actions = Published().SelectMany(d => d.FeatureActions.Select(a => (Descriptor: d, Action: a))).ToList();

        actions.Should().NotBeEmpty();
        foreach (var (descriptor, action) in actions)
        {
            action.Route.Should().StartWith(descriptor.ApiBaseRoute, descriptor.OrderTypeId);
            action.Route.Should().StartWith("/api/v/neutral-operator/", descriptor.OrderTypeId);
        }
    }

    [Fact]
    public void TheDescriptors_WithTheGatewayRoute_StillPassTheKernelsValidation() =>
        OrderTypeDescriptorValidation.FindProblem(NeutralOperatorModule.ModuleId, Published()).Should().BeNull();

    [Fact]
    public async Task TheHost_PublishesTheGatewayRoute_OfEveryOrderType_ToTheKernelsRegistry()
    {
        await using var host = await NeutralOperatorHost.StartAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (host.Kernel.To(HttpMethod.Put, "/order-types").Count == 0)
            await Task.Delay(25, timeout.Token);

        var registration = host.Kernel.To(HttpMethod.Put, "/order-types")[^1];

        var routes = registration.Json.EnumerateArray().Select(d => d.GetProperty("apiBaseRoute").GetString()).ToList();

        routes.Should().NotBeEmpty().And.OnlyContain(route => string.Equals(route, GatewayRoute, StringComparison.Ordinal));
        registration.Path.Should().Be("/api/registry/verticals/neutral-operator/order-types");
        registration.Json.ValueKind.Should().Be(JsonValueKind.Array);
    }
}
