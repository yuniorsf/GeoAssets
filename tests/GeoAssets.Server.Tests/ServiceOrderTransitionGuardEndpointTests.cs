using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using GeoAssets.Workflow.Orders;
using Xunit;
using static GeoAssets.Server.Tests.OrderTargetsApiAssertions;

namespace GeoAssets.Server.Tests;

/// <summary>
/// XD01-362 — a transition an <see cref="ITransitionGuard"/> denies is a 409 problem carrying the guard's own code, its reason
/// and the transition, on both routes that change an order's status (PUT and the default action append).
/// </summary>
public class ServiceOrderTransitionGuardEndpointTests
{
    private const string GuardCode = "test.guard-denied";
    private const string GuardReason = "The guard says no.";

    private sealed class DenyingGuard : ITransitionGuard
    {
        public Task<GuardVerdict> EvaluateAsync(OrderTransitionContext context, CancellationToken cancellationToken) =>
            Task.FromResult(GuardVerdict.Deny(GuardCode, GuardReason));
    }

    private static Task<OrderTargetsApiHost> StartAsync() =>
        OrderTargetsApiHost.StartAsync(
            mapOrderRoutes: true,
            decorateOrders: inner => new ValidatingServiceOrderRepository(inner, guards: [new DenyingGuard()], timeProvider: TimeProvider.System));

    [Fact]
    public async Task Put_GuardDenies_Returns409ProblemWithGuardCode()
    {
        await using var host = await StartAsync();
        await host.SeedOrderAsync("ord-1");
        using var client = host.ClientFor("admin");
        var edited = new ServiceOrder
        {
            Id = "ord-1", Title = "ord-1", OrderTypeId = OrderTargetsApiHost.PoleSurvey, Status = ServiceOrderStatus.Pending,
            CreatedBy = "someone-else", OrganizationId = OrderTargetsApiHost.OrgA, CreatedAt = OrderTargetsApiHost.Now.UtcDateTime.AddDays(-1),
        };

        var response = await client.PutAsJsonAsync(Path("service-orders/ord-1"), edited);

        await AssertGuardProblemAsync(response, ServiceOrderStatus.Draft, ServiceOrderStatus.Pending);
        (await host.Orders.GetByIdAsync("ord-1"))!.Status.Should().Be(ServiceOrderStatus.Draft);
    }

    [Fact]
    public async Task Actions_GuardDenies_Returns409ProblemWithGuardCode()
    {
        await using var host = await StartAsync();
        await host.SeedOrderAsync("ord-1");
        using var client = host.ClientFor("admin");
        var cancel = new OrderActionLog(
            OrderActionType.Cancel, "me", OrderTargetsApiHost.Now.UtcDateTime, ResultingStatus: ServiceOrderStatus.Cancelled);

        var response = await client.PostJsonAsync("service-orders/ord-1/actions", cancel);

        await AssertGuardProblemAsync(response, ServiceOrderStatus.Draft, ServiceOrderStatus.Cancelled);
        (await host.Orders.GetByIdAsync("ord-1"))!.Status.Should().Be(ServiceOrderStatus.Draft);
    }

    private static async Task AssertGuardProblemAsync(HttpResponseMessage response, string from, string to)
    {
        var body = await response.ReadJsonAsync(HttpStatusCode.Conflict);
        body.GetProperty("code").GetString().Should().Be(GuardCode);
        body.GetProperty("detail").GetString().Should().Be(GuardReason);
        body.GetProperty("from").GetString().Should().Be(from);
        body.GetProperty("to").GetString().Should().Be(to);
    }
}
