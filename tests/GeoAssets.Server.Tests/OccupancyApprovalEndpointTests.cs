using System.Net;
using FluentAssertions;
using GeoAssets.NeutralOperator.Domain.Occupancy;
using GeoAssets.Core.Messaging.Events;
using GeoAssets.NeutralOperator.Application.SharedUse;
using GeoAssets.NeutralOperator.Application.Occupancy;
using GeoAssets.NeutralOperator.Application.Occupancy.Saga;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using GeoAssets.Workflow.TestKit;
using Xunit;
using static GeoAssets.Server.Tests.OrderTargetsApiAssertions;

namespace GeoAssets.Server.Tests;

/// <summary>
/// <c>POST /service-orders/{id}/actions</c> with <c>Approve</c> on an order whose type declares a
/// target policy (XD01-318): it goes through the real <see cref="OccupancyCommitService"/>, which
/// commits what was staged on the order's poles. Every answer is in the action route's own
/// dialect, plus the <c>occupancy.concurrent-modification</c> problem.
/// </summary>
public sealed class OccupancyApprovalEndpointTests
{
    private const string Order = SharedUseApiSeed.Order;
    private const string Pole = "f-001";

    private static object Approve(string? resultingStatus = null) => new
    {
        action = (int)OrderActionType.Approve,
        performedBy = "whoever-the-body-says",
        performedAt = OrderTargetsApiHost.Now.UtcDateTime,
        resultingStatus,
    };

    private static async Task<OrderTargetsApiHost> StartAsync(
        string status = ServiceOrderStatus.Completed, Func<IServiceOrderRepository, IServiceOrderRepository>? decorateOrders = null)
    {
        var host = await SharedUseApiSeed.StartAsync(status: status, poles: 1, occupied: false, mapOccupancyRoutes: true, decorateOrders: decorateOrders);
        return host;
    }

    private static async Task<OccupancyChangeSet> StageAsync(OrderTargetsApiHost host, params OccupancyChange[] changes)
    {
        var owner = host.Occupancy.ForOrganization(OrderTargetsApiHost.OrgA);
        var baseline = await owner.GetForPoleAsync(Pole, CancellationToken.None);
        var set = OccupancyChangeSet.Stage(
            Order, $"{Order}-{Pole}", Pole, OrderTargetsApiHost.OrgA, changes, baseline, "crew-1", OrderTargetsApiHost.Now);
        return await host.ChangeSets.ForOrganization(OrderTargetsApiHost.OrgA).StageAsync(set, CancellationToken.None);
    }

    [Fact]
    public async Task Approve_ByASupervisorOfThePoleOwner_Returns204_WritesTheStagedSetAsPending_StagesTheBatch_AndTheOrderIsApprovedOnlyOnceTheCatalogApplied()
    {
        await using var host = await StartAsync();
        await StageAsync(host, OccupancyChangeBuilder.Add(1), OccupancyChangeBuilder.AddBeyondRegulatedSlots());
        using var client = host.ClientFor("supervisor-a");

        var response = await client.PostJsonAsync($"service-orders/{Order}/actions", Approve(SharedUseOrderTypes.ApprovedStateKey));

        // Step one: the approval is accepted, the poles carry the occupancy, the batch is staged — and the order awaits the catalog.
        response.StatusCode.Should().Be(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        var awaiting = (await host.Orders.GetByIdAsync(Order))!;
        awaiting.Status.Should().Be(ServiceOrderStatus.Completed, "approved, awaiting apply: the status moves when the kernel answers");
        OccupancyApplySagaAttributes.IsAwaitingApply(awaiting.Attributes).Should().BeTrue();
        awaiting.ActionLog.Should().ContainSingle().Which.PerformedBy
            .Should().Be(OrderTargetsApiHost.Identities["supervisor-a"].UserId.ToString(), "the approval is logged as the caller, never as the body says");
        var pole = (await host.Occupancy.ForOrganization(OrderTargetsApiHost.OrgA).GetForPoleAsync(Pole, CancellationToken.None))!;
        pole.Entries.Should().HaveCount(2);
        pole.OverCapacityEntries.Should().ContainSingle();
        host.ChangeSets.Snapshot().Should().ContainSingle().Which.Status.Should().Be(OccupancyChangeSetStatus.Pending);
        var batch = (AssetMutationBatchRequestedEvent)host.Outbox.Messages.Should().ContainSingle().Subject.Message;
        batch.CatalogInstanceId.Should().Be(OrderOccupancyOptions.DefaultCatalogInstanceId);
        batch.SourceVerticalId.Should().Be("neutral-operator");
        batch.Mutations.Should().ContainSingle().Which.CustomAttributes[OccupancyAssetAttributes.ClassKey].Should().Be("Low");

        // Step two: the kernel applies the batch; the saga finalizes the approval in process.
        (await host.ReplyAppliedAsync()).Outcome.Should().Be(OccupancyApplySagaOutcome.Committed);
        var approved = (await host.Orders.GetByIdAsync(Order))!;
        approved.Status.Should().Be(SharedUseOrderTypes.ApprovedStateKey);
        approved.ActionLog.Select(entry => entry.Action).Should().Equal(OrderActionType.Approve, OccupancyApplyActions.Applied);
        host.ChangeSets.Snapshot().Should().ContainSingle().Which.Status.Should().Be(OccupancyChangeSetStatus.Committed);
    }

    [Fact]
    public async Task Approve_Twice_BeforeTheCatalogAnswers_Returns409ApplyPending_AndStagesNothingMore()
    {
        await using var host = await StartAsync();
        await StageAsync(host, OccupancyChangeBuilder.Add(1));
        using var client = host.ClientFor("supervisor-a");
        (await client.PostJsonAsync($"service-orders/{Order}/actions", Approve())).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var second = await client.PostJsonAsync($"service-orders/{Order}/actions", Approve());

        var problem = await second.ShouldBeProblemAsync(HttpStatusCode.Conflict, OccupancyProblemCodes.ApplyPending);
        problem.GetProperty("poleFeatureIds").GetArrayLength().Should().Be(0, "the order itself awaits the catalog");
        host.Outbox.Messages.Should().ContainSingle();
        (await host.Orders.GetByIdAsync(Order))!.ActionLog.Should().ContainSingle();
    }

    [Fact]
    public async Task Approve_AnOrderWhosePoleAnotherPendingApprovalHolds_Returns409ApplyPending_NamingThePole()
    {
        await using var host = await StartAsync();
        await StageAsync(host, OccupancyChangeBuilder.Add(1));
        using var client = host.ClientFor("supervisor-a");
        (await client.PostJsonAsync($"service-orders/{Order}/actions", Approve())).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var other = await host.SeedOrderAsync(
            "ord-2", SharedUseOrderTypes.PoleWorkId, ServiceOrderStatus.Completed,
            createdBy: OrderTargetsApiHost.Identities["creator"].UserId.ToString());
        await SharedUseApiSeed.SeedTargetsAsync(host, "ord-2", [SharedUseApiSeed.Activity], Pole);
        var baseline = await host.Occupancy.ForOrganization(OrderTargetsApiHost.OrgA).GetForPoleAsync(Pole, CancellationToken.None);
        await host.ChangeSets.ForOrganization(OrderTargetsApiHost.OrgA).StageAsync(
            OccupancyChangeSet.Stage("ord-2", $"ord-2-{Pole}", Pole, OrderTargetsApiHost.OrgA, [OccupancyChangeBuilder.Add(2)], baseline, "crew-1", OrderTargetsApiHost.Now),
            CancellationToken.None);

        var response = await client.PostJsonAsync($"service-orders/{other.Id}/actions", Approve());

        var problem = await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, OccupancyProblemCodes.ApplyPending);
        problem.GetProperty("poleFeatureIds").Strings().Should().Equal(Pole);
        (await host.Orders.GetByIdAsync("ord-2"))!.ActionLog.Should().BeEmpty();
    }

    [Fact]
    public async Task Approve_WhenTheCatalogRejects_TheSagaCompensates_TheOrderStaysCompleted_AndAFreshApprovalStagesAnAttemptTwoBatch()
    {
        await using var host = await StartAsync();
        await StageAsync(host, OccupancyChangeBuilder.Add(1));
        using var client = host.ClientFor("supervisor-a");
        (await client.PostJsonAsync($"service-orders/{Order}/actions", Approve())).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await host.ReplyRejectedAsync("feature-not-found", "Feature 'f-001' not found for UpdateAttributes.", Pole)).Outcome
            .Should().Be(OccupancyApplySagaOutcome.Reverted);

        var order = (await host.Orders.GetByIdAsync(Order))!;
        order.Status.Should().Be(ServiceOrderStatus.Completed);
        order.Attributes.Should().Contain(OccupancyApplySagaAttributes.StateKey, OccupancyApplySagaAttributes.Reverted);
        (await host.Occupancy.ForOrganization(OrderTargetsApiHost.OrgA).GetForPoleAsync(Pole, CancellationToken.None))!.Entries
            .Should().BeEmpty("the pole holds what it held before the approval: nothing");
        host.ChangeSets.Snapshot().Should().ContainSingle().Which.Status.Should().Be(OccupancyChangeSetStatus.Reverted);
        var reverted = await (await client.GetAsync(new Uri($"service-orders/{Order}/occupancy-changes?status=reverted", UriKind.Relative))).ReadJsonAsync();
        reverted.GetProperty("totalCount").GetInt32().Should().Be(1);

        await StageAsync(host, OccupancyChangeBuilder.Add(2));
        (await client.PostJsonAsync($"service-orders/{Order}/actions", Approve())).StatusCode.Should().Be(HttpStatusCode.NoContent);
        host.StagedBatches().Select(batch => batch.BatchId).Should().Equal(
            OccupancyCommitService.AssetMutationBatchId(Order, attempt: 1), OccupancyCommitService.AssetMutationBatchId(Order, attempt: 2));
    }

    [Fact]
    public async Task GetChanges_StatusPending_ListsTheSetsAnApprovalWroteAndTheCatalogHasNotAnswered()
    {
        await using var host = await StartAsync();
        await StageAsync(host, OccupancyChangeBuilder.Add(1));
        using var client = host.ClientFor("supervisor-a");
        (await client.PostJsonAsync($"service-orders/{Order}/actions", Approve())).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var pending = await (await client.GetAsync(new Uri($"service-orders/{Order}/occupancy-changes?status=pending", UriKind.Relative))).ReadJsonAsync();
        var staged = await (await client.GetAsync(new Uri($"service-orders/{Order}/occupancy-changes", UriKind.Relative))).ReadJsonAsync();

        pending.GetProperty("items").EnumerateArray().Should().ContainSingle()
            .Which.GetProperty("status").GetInt32().Should().Be((int)OccupancyChangeSetStatus.Pending);
        staged.GetProperty("totalCount").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task Approve_WithoutAResultingStatus_GoesWhereTheApprovalLeads()
    {
        await using var host = await StartAsync();
        using var client = host.ClientFor("supervisor-a");

        var response = await client.PostJsonAsync($"service-orders/{Order}/actions", Approve());

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await host.Orders.GetByIdAsync(Order))!.Status.Should().Be(SharedUseOrderTypes.ApprovedStateKey);
        host.Outbox.Messages.Should().BeEmpty("nothing was staged, so no pole was written");
    }

    [Fact]
    public async Task Approve_APoleThatMovedSinceStaging_Returns409ConcurrentModification_NamingThePole_AndCommitsNothing()
    {
        await using var host = await StartAsync();
        await StageAsync(host, OccupancyChangeBuilder.Add(1));
        await SharedUseApiSeed.OccupyAsync(host, Pole, SharedUseApiSeed.Entry(Pole, 2, OrderTargetsApiHost.OrgC));
        using var client = host.ClientFor("supervisor-a");

        var response = await client.PostJsonAsync($"service-orders/{Order}/actions", Approve());

        var problem = await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, OccupancyProblemCodes.ConcurrentModification);
        problem.GetProperty("poleFeatureIds").Strings().Should().Equal(Pole);
        (await host.Orders.GetByIdAsync(Order))!.Status.Should().Be(ServiceOrderStatus.Completed);
        host.Outbox.Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task Approve_ByAnOccupantTheOrderWasDispatchedTo_Returns403_AndCommitsNothing()
    {
        await using var host = await StartAsync();
        await StageAsync(host, OccupancyChangeBuilder.Add(1));
        await host.Orders.AppendDispatchAsync(
            Order, new OrderDispatch(OrderTargetsApiHost.OrgC.ToString("D"), DispatchTargetType.Organization, "planner", OrderTargetsApiHost.Now.UtcDateTime));
        using var client = host.ClientFor("occupant");

        var response = await client.PostJsonAsync($"service-orders/{Order}/actions", Approve());

        var body = await response.ReadJsonAsync(HttpStatusCode.Forbidden);
        body.GetProperty("reason").GetString().Should().Be(
            ServiceOrderRules.RecipientOrderIsReadOnlyReason, "XD01-333: the order's type only informs a recipient, so the rules refuse it before the owner-only check");
        (await host.Orders.GetByIdAsync(Order))!.Status.Should().Be(ServiceOrderStatus.Completed);
    }

    [Fact]
    public async Task Approve_ByAnAdministratorOfAnotherOrganization_Returns404_AsForAnUnknownOrder()
    {
        await using var host = await StartAsync();
        using var client = host.ClientFor("admin-b");

        var response = await client.PostJsonAsync($"service-orders/{Order}/actions", Approve());

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await host.Orders.GetByIdAsync(Order))!.Status.Should().Be(ServiceOrderStatus.Completed);
    }

    [Fact]
    public async Task Approve_ByAMemberTheRulesRefuse_Returns403WithTheRulesReason()
    {
        await using var host = await StartAsync();
        using var client = host.ClientFor("creator");

        var response = await client.PostJsonAsync($"service-orders/{Order}/actions", Approve());

        (await response.ReadJsonAsync(HttpStatusCode.Forbidden)).GetProperty("reason").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Approve_StatingAResultingStatusOtherThanWhereTheApprovalLeads_Returns400FromTo_AndCommitsNothing()
    {
        await using var host = await StartAsync();
        await StageAsync(host, OccupancyChangeBuilder.Add(1));
        using var client = host.ClientFor("supervisor-a");

        var response = await client.PostJsonAsync($"service-orders/{Order}/actions", Approve(ServiceOrderStatus.Cancelled));

        var body = await response.ReadJsonAsync(HttpStatusCode.BadRequest);
        body.GetProperty("from").GetString().Should().Be(ServiceOrderStatus.Completed);
        body.GetProperty("to").GetString().Should().Be(ServiceOrderStatus.Cancelled);
        (await host.Orders.GetByIdAsync(Order))!.Status.Should().Be(ServiceOrderStatus.Completed);
    }

    [Fact]
    public async Task Approve_FromAStatusWithNoApproval_Returns400FromTo()
    {
        await using var host = await StartAsync(ServiceOrderStatus.InProgress);
        using var client = host.ClientFor("supervisor-a");

        var response = await client.PostJsonAsync($"service-orders/{Order}/actions", Approve());

        var body = await response.ReadJsonAsync(HttpStatusCode.BadRequest);
        body.GetProperty("from").GetString().Should().Be(ServiceOrderStatus.InProgress);
        body.GetProperty("to").GetString().Should().Be(ServiceOrderStatus.InProgress);
    }

    [Fact]
    public async Task Approve_WhenTheOrderChangesUnderTheApproval_Returns409()
    {
        await using var host = await StartAsync(decorateOrders: orders => new FailingOrders(orders, onUpdate: new ServiceOrderConcurrencyException(Order)));
        using var client = host.ClientFor("supervisor-a");

        var response = await client.PostJsonAsync($"service-orders/{Order}/actions", Approve());

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().BeEmpty("the action route's plain conflict, as for any order changed meanwhile");
    }

    [Fact]
    public async Task Approve_WhenTheOrderIsGoneByTheTimeTheServiceReadsIt_Returns404()
    {
        await using var host = await StartAsync(decorateOrders: orders => new FailingOrders(orders, vanishAfterFirstRead: true));
        using var client = host.ClientFor("supervisor-a");

        var response = await client.PostJsonAsync($"service-orders/{Order}/actions", Approve());

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Approve_AFailureThatIsNotTheCallers_IsNotTurnedIntoAClientError()
    {
        await using var host = await StartAsync(decorateOrders: orders => new FailingOrders(orders, onUpdate: new TimeoutException("database")));
        using var client = host.ClientFor("supervisor-a");

        var act = async () =>
        {
            using var response = await client.PostJsonAsync($"service-orders/{Order}/actions", Approve());
        };

        await act.Should().ThrowAsync<TimeoutException>();
    }

    [Fact]
    public async Task Approve_OnAnOrderWhoseTypeHasNoTargetPolicy_IsStillAPlainActionEntry()
    {
        await using var host = await OrderTargetsApiHost.StartAsync(mapOrderRoutes: true);
        host.OrderTypes.Register(new OrderType { Id = "plain-review", DisplayName = "Plain review" });
        await host.SeedOrderAsync("ord-plain", "plain-review", ServiceOrderStatus.InProgress);
        using var client = host.ClientFor("supervisor-a");

        var response = await client.PostJsonAsync("service-orders/ord-plain/actions", Approve());

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var order = (await host.Orders.GetByIdAsync("ord-plain"))!;
        order.Status.Should().Be(ServiceOrderStatus.InProgress);
        order.ActionLog.Should().ContainSingle().Which.PerformedBy.Should().Be("whoever-the-body-says", "the plain path stores the entry as sent, as before");
        host.Outbox.Messages.Should().BeEmpty();
    }

    /// <summary>The order store, failing the approval's update with <c>onUpdate</c>, or answering "no such order" to every read after the first.</summary>
    private sealed class FailingOrders(IServiceOrderRepository inner, Exception? onUpdate = null, bool vanishAfterFirstRead = false) : IServiceOrderRepository
    {
        private int _reads;

        public event EventHandler<IServiceOrder>? OrderAdded { add => inner.OrderAdded += value; remove => inner.OrderAdded -= value; }
        public event EventHandler<IServiceOrder>? OrderUpdated { add => inner.OrderUpdated += value; remove => inner.OrderUpdated -= value; }
        public event EventHandler<(IServiceOrder Order, string Previous)>? OrderStatusChanged { add => inner.OrderStatusChanged += value; remove => inner.OrderStatusChanged -= value; }
        public event EventHandler<string>? OrderDeleted { add => inner.OrderDeleted += value; remove => inner.OrderDeleted -= value; }

        public Task<IServiceOrder?> GetByIdAsync(string id, CancellationToken ct = default) =>
            vanishAfterFirstRead && Interlocked.Increment(ref _reads) > 1 ? Task.FromResult<IServiceOrder?>(null) : inner.GetByIdAsync(id, ct);

        public Task UpdateAsync(IServiceOrder order, CancellationToken ct = default) =>
            onUpdate is not null ? Task.FromException(onUpdate) : inner.UpdateAsync(order, ct);

        public Task<IReadOnlyList<IServiceOrder>> GetAllAsync(CancellationToken ct = default) => inner.GetAllAsync(ct);
        public Task<IReadOnlyList<IServiceOrder>> GetRootsAsync(CancellationToken ct = default) => inner.GetRootsAsync(ct);
        public Task<IReadOnlyList<IServiceOrder>> GetChildrenAsync(string parentId, CancellationToken ct = default) => inner.GetChildrenAsync(parentId, ct);
        public Task<IServiceOrder?> GetParentAsync(string childId, CancellationToken ct = default) => inner.GetParentAsync(childId, ct);
        public Task<IReadOnlyList<IServiceOrder>> GetByStatusAsync(string status, CancellationToken ct = default) => inner.GetByStatusAsync(status, ct);
        public Task<IReadOnlyList<IServiceOrder>> GetByAssigneeAsync(string userId, CancellationToken ct = default) => inner.GetByAssigneeAsync(userId, ct);
        public Task<IReadOnlyList<IServiceOrder>> GetByCreatorAsync(string userId, CancellationToken ct = default) => inner.GetByCreatorAsync(userId, ct);
        public Task<IReadOnlyList<IServiceOrder>> GetByOrderTypeAsync(string orderTypeId, CancellationToken ct = default) => inner.GetByOrderTypeAsync(orderTypeId, ct);
        public Task<IReadOnlyList<IServiceOrder>> GetByDateRangeAsync(DateTime from, DateTime toExclusive, CancellationToken ct = default) => inner.GetByDateRangeAsync(from, toExclusive, ct);
        public Task<IReadOnlyList<IServiceOrder>> GetDispatchedToAsync(string targetId, DispatchTargetType targetType, CancellationToken ct = default) => inner.GetDispatchedToAsync(targetId, targetType, ct);
        public Task AddAsync(IServiceOrder order, CancellationToken ct = default) => inner.AddAsync(order, ct);
        public Task AppendDispatchAsync(string orderId, OrderDispatch dispatch, CancellationToken ct = default) => inner.AppendDispatchAsync(orderId, dispatch, ct);
        public Task AppendActionAsync(string orderId, OrderActionLog entry, CancellationToken ct = default) => inner.AppendActionAsync(orderId, entry, ct);
        public Task DeleteAsync(string id, CancellationToken ct = default) => inner.DeleteAsync(id, ct);
    }
}
