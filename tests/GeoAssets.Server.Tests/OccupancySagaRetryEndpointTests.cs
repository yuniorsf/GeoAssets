using System.Net;
using FluentAssertions;
using GeoAssets.Core.Messaging.Events;
using GeoAssets.NeutralOperator.Application.Occupancy;
using GeoAssets.NeutralOperator.Application.Occupancy.Saga;
using GeoAssets.NeutralOperator.Domain.Occupancy;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using GeoAssets.Workflow.Targets;
using GeoAssets.Workflow.TestKit;
using Xunit;
using static GeoAssets.Server.Tests.OrderTargetsApiAssertions;

namespace GeoAssets.Server.Tests;

/// <summary>
/// <c>POST /service-orders/{id}/occupancy-saga/retry</c> (XD01-383) through the real route in a TestServer, over the TestKit's
/// in-memory stores: the operator's handle on an approval whose batch the kernel has not answered. Authorized as the approval is.
/// </summary>
public sealed class OccupancySagaRetryEndpointTests
{
    private const string Order = SharedUseApiSeed.Order;
    private const string Pole = "f-001";
    private const string Retry = $"service-orders/{Order}/occupancy-saga/retry";

    private static object Approve() => new
    {
        action = (int)OrderActionType.Approve,
        performedBy = "whoever-the-body-says",
        performedAt = OrderTargetsApiHost.Now.UtcDateTime,
    };

    private static async Task<OrderTargetsApiHost> StartApprovedAsync()
    {
        var host = await SharedUseApiSeed.StartAsync(status: ServiceOrderStatus.Completed, poles: 1, occupied: false, mapOccupancyRoutes: true);
        var set = OccupancyChangeSet.Stage(
            Order, $"{Order}-{Pole}", Pole, OrderTargetsApiHost.OrgA, [OccupancyChangeBuilder.Add(1)], baseline: null, "crew-1", OrderTargetsApiHost.Now);
        await host.ChangeSets.ForOrganization(OrderTargetsApiHost.OrgA).StageAsync(set, CancellationToken.None);
        using var supervisor = host.ClientFor("supervisor-a");
        (await supervisor.PostJsonAsync($"service-orders/{Order}/actions", Approve())).StatusCode.Should().Be(HttpStatusCode.NoContent);
        return host;
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string path = Retry) => client.PostAsync(Path(path), content: null);

    [Fact]
    public async Task Retry_ByASupervisorOfThePoleOwner_Returns202WithTheSaga_RestagesTheSameBatch_CountsTheRetry_AndRestartsTheTimeout()
    {
        await using var host = await StartApprovedAsync();
        var original = host.StagedBatches().Single();
        host.Clock.Advance(TimeSpan.FromMinutes(20));
        (await host.Sagas.MarkTimedOutAsync(staleBefore: host.Clock.GetUtcNow(), now: host.Clock.GetUtcNow(), max: 10, CancellationToken.None))
            .Should().ContainSingle("the saga timed out, as the sweeper would have flagged it");
        using var client = host.ClientFor("supervisor-a");

        var response = await PostAsync(client);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());
        var view = await response.ReadJsonAsync(HttpStatusCode.Accepted);
        view.GetProperty("batchId").GetGuid().Should().Be(original.BatchId);
        view.GetProperty("orderId").GetString().Should().Be(Order);
        view.GetProperty("attempt").GetInt32().Should().Be(1);
        view.GetProperty("state").GetInt32().Should().Be((int)OccupancyApplySagaState.Pending);
        view.GetProperty("retryCount").GetInt32().Should().Be(1);
        view.GetProperty("lastRequestedAt").GetDateTimeOffset().Should().Be(host.Clock.GetUtcNow(), "the timeout restarts from the retry");
        (!view.TryGetProperty("timedOutAt", out var timedOut) || timedOut.ValueKind == System.Text.Json.JsonValueKind.Null)
            .Should().BeTrue("the retry cleared the timeout flag");

        var staged = host.StagedBatches();
        staged.Should().HaveCount(2);
        staged[1].Should().BeEquivalentTo(original, "the retry is the same batch: the kernel applies it at most once");
        var saga = (await host.Sagas.FindLatestForOrderAsync(Order, CancellationToken.None))!;
        saga.RetryCount.Should().Be(1);
        saga.TimedOutAt.Should().BeNull();
        saga.State.Should().Be(OccupancyApplySagaState.Pending);
        (await host.Orders.GetByIdAsync(Order))!.ActionLog.Select(entry => entry.Action).Should().Equal(OrderActionType.Approve, OccupancyApplyActions.Retried);
    }

    [Fact]
    public async Task Retry_Twice_CountsBothRetries_AndStagesTheBatchEachTime()
    {
        await using var host = await StartApprovedAsync();
        using var client = host.ClientFor("supervisor-a");

        await PostAsync(client);
        var second = await (await PostAsync(client)).ReadJsonAsync(HttpStatusCode.Accepted);

        second.GetProperty("retryCount").GetInt32().Should().Be(2);
        host.StagedBatches().Select(batch => batch.BatchId).Distinct().Should().ContainSingle();
        host.StagedBatches().Should().HaveCount(3);
    }

    [Fact]
    public async Task Retry_WithNoApprovalYet_Returns409ApplyNotPending_AndStagesNothing()
    {
        await using var host = await SharedUseApiSeed.StartAsync(status: ServiceOrderStatus.Completed, poles: 1, occupied: false, mapOccupancyRoutes: true);
        using var client = host.ClientFor("supervisor-a");

        var response = await PostAsync(client);

        var problem = await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, OccupancyProblemCodes.ApplyNotPending);
        (!problem.TryGetProperty("state", out var state) || state.ValueKind == System.Text.Json.JsonValueKind.Null)
            .Should().BeTrue("there was no saga, so there is no state to name");
        host.StagedBatches().Should().BeEmpty();
    }

    [Fact]
    public async Task Retry_AfterTheCatalogApplied_Returns409ApplyNotPending_NamingTheState_AndStagesNothingMore()
    {
        await using var host = await StartApprovedAsync();
        await host.ReplyAppliedAsync();
        using var client = host.ClientFor("supervisor-a");

        var response = await PostAsync(client);

        var problem = await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, OccupancyProblemCodes.ApplyNotPending);
        problem.GetProperty("state").GetString().Should().Be(nameof(OccupancyApplySagaState.Committed));
        host.StagedBatches().Should().ContainSingle();
    }

    [Fact]
    public async Task Retry_AfterARejection_Returns409ApplyNotPending_BecauseTheApprovalWasCompensated()
    {
        await using var host = await StartApprovedAsync();
        await host.ReplyRejectedAsync(AssetMutationBatchRejectionCodes.FeatureNotFound, "gone", Pole);
        using var client = host.ClientFor("supervisor-a");

        var response = await PostAsync(client);

        (await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, OccupancyProblemCodes.ApplyNotPending)).GetProperty("state").GetString()
            .Should().Be(nameof(OccupancyApplySagaState.Reverted));
    }

    [Fact]
    public async Task Retry_UnknownOrder_Returns404OrdersNotFound()
    {
        await using var host = await StartApprovedAsync();
        using var client = host.ClientFor("supervisor-a");

        var response = await PostAsync(client, "service-orders/ord-0/occupancy-saga/retry");

        (await response.ShouldBeProblemAsync(HttpStatusCode.NotFound, WorkflowProblemCodes.OrderNotFound)).GetProperty("orderId").GetString().Should().Be("ord-0");
    }

    [Fact]
    public async Task Retry_ByAnAdministratorOfAnotherOrganization_GetsTheSameResponseAsForAnUnknownOrder_AndStagesNothing()
    {
        await using var host = await StartApprovedAsync();
        using var client = host.ClientFor("admin-b");

        var foreign = await PostAsync(client);
        var unknown = await PostAsync(client, "service-orders/ord-0/occupancy-saga/retry");

        var problem = await foreign.ShouldBeProblemAsync(HttpStatusCode.NotFound, WorkflowProblemCodes.OrderNotFound);
        problem.TryGetProperty("reason", out _).Should().BeFalse("nothing tells the outsider the order exists");
        (await foreign.Content.ReadAsStringAsync()).Replace(Order, "ord-0", StringComparison.Ordinal).Should().Be(await unknown.Content.ReadAsStringAsync());
        host.StagedBatches().Should().ContainSingle();
        (await host.Sagas.FindLatestForOrderAsync(Order, CancellationToken.None))!.RetryCount.Should().Be(0);
    }

    [Theory]
    [InlineData("reader")]
    [InlineData("creator")]
    [InlineData("crew-a")]
    public async Task Retry_ByAMemberOfTheOrganizationWhoseRolesDoNotApprove_Returns403WithTheRulesReason_AndStagesNothing(string token)
    {
        await using var host = await StartApprovedAsync();
        using var client = host.ClientFor(token);

        var response = await PostAsync(client);

        var problem = await response.ShouldBeProblemAsync(HttpStatusCode.Forbidden, WorkflowProblemCodes.Denied);
        problem.GetProperty("reason").GetString().Should().NotBeNullOrWhiteSpace();
        host.StagedBatches().Should().ContainSingle();
    }

    [Fact]
    public async Task Retry_ByACrossOrganizationGranteeWhoMayApprove_Returns403OwnerOnly_AndStagesNothing()
    {
        await using var host = await StartApprovedAsync();
        host.Grants.Active.Add(new()
        {
            GranteeOrganizationId = OrderTargetsApiHost.OrgB,
            ResourceOrganizationId = OrderTargetsApiHost.OrgA,
            ResourceType = "ServiceOrder",
            AllowedActions = { "serviceorders:view", "serviceorders:approve" },
        });
        using var client = host.ClientFor("admin-b");

        var response = await PostAsync(client);

        var problem = await response.ShouldBeProblemAsync(HttpStatusCode.Forbidden, WorkflowProblemCodes.Denied);
        problem.GetProperty("reason").GetString().Should().Contain("organization that owns");
        host.StagedBatches().Should().ContainSingle();
    }

    [Fact]
    public async Task Retry_WithoutAToken_Returns401()
    {
        await using var host = await StartApprovedAsync();
        using var client = host.ClientFor(token: null);

        var response = await PostAsync(client);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        host.StagedBatches().Should().ContainSingle();
    }
}
