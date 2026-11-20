using System.Net;
using FluentAssertions;
using GeoAssets.NeutralOperator.Application.Occupancy;
using GeoAssets.NeutralOperator.Server.Tests.Support;
using GeoAssets.Workflow.Targets;
using Xunit;

namespace GeoAssets.NeutralOperator.Server.Tests.Http;

/// <summary>
/// <c>POST /service-orders/{id}/occupancy-saga/retry</c> (XD01-383) on the NeutralOperator host, over its own database: the
/// route is authorized as the approval is, and an order with no pending approval saga has nothing to retry. The saga's own
/// behaviour is the vertical's, run end to end in <c>GeoAssets.Security.Tests</c>.
/// </summary>
public sealed class OccupancySagaRetryTests
{
    private const string Order = "no-saga-1";

    private static Uri Retry(string orderId) => NeutralOperatorApi.Route($"service-orders/{orderId}/occupancy-saga/retry");

    [Fact]
    public async Task Retry_WithoutAToken_Is401()
    {
        await using var host = await NeutralOperatorHost.StartAsync();
        using var anonymous = host.ClientFor(null);

        var response = await anonymous.PostAsync(Retry(Order), content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Retry_ForAnUnknownOrder_Is404OrderNotFound()
    {
        await using var host = await NeutralOperatorHost.StartAsync();
        using var owner = host.ClientFor("owner-admin");

        var response = await owner.PostAsync(Retry("no-such-order"), content: null);

        await response.ShouldBeProblemAsync(HttpStatusCode.NotFound, WorkflowProblemCodes.OrderNotFound);
    }

    [Fact]
    public async Task Retry_ByACallerOfAnotherOrganization_Is404_JustAsForAnUnknownOrder()
    {
        await using var host = await NeutralOperatorHost.StartAsync();
        using var owner = host.ClientFor("owner-admin");
        using var outsider = host.ClientFor("outsider-admin");
        await owner.CreateOrderAsync(Order);

        var foreign = await outsider.PostAsync(Retry(Order), content: null);
        var unknown = await outsider.PostAsync(Retry("no-such-order"), content: null);

        await foreign.ShouldBeProblemAsync(HttpStatusCode.NotFound, WorkflowProblemCodes.OrderNotFound);
        (await foreign.Content.ReadAsStringAsync()).Replace(Order, "no-such-order", StringComparison.Ordinal).Should().Be(await unknown.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Retry_ForAnOrderWithNoApprovalSaga_Is409ApplyNotPending_AndStagesNothing()
    {
        await using var host = await NeutralOperatorHost.StartAsync();
        using var owner = host.ClientFor("owner-admin");
        await owner.CreateOrderAsync(Order);

        var response = await owner.PostAsync(Retry(Order), content: null);

        await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, OccupancyProblemCodes.ApplyNotPending);
        (await DatabaseProbe.ValueAsync(host.ConnectionString, """SELECT count(*)::text AS "Value" FROM "WorkflowOutboxMessages" """)).Should().Be("0");
    }
}
