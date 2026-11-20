using System.Diagnostics;
using FluentAssertions;
using GeoAssets.Core.Messaging;
using GeoAssets.Core.Messaging.Events;
using GeoAssets.Infrastructure.Observability;
using GeoAssets.NeutralOperator.Application.Occupancy;
using GeoAssets.NeutralOperator.Application.Occupancy.Saga;
using GeoAssets.NeutralOperator.Application.SharedUse;
using GeoAssets.NeutralOperator.Domain.Occupancy;
using GeoAssets.Server;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.TestKit;
using Xunit;

namespace GeoAssets.Security.Tests;

/// <summary>
/// XD01-383's fault-injection tests, end to end (<see cref="ApprovalSagaHost"/>): the real NeutralOperator approval and saga on
/// the EF stores, the kernel's consumer over the real catalog, the in-process transport, on PostgreSQL. A kernel that is down
/// leaves the approval pending and the catalog — and so the map — untouched, and the batch is applied exactly once when it
/// comes back; a kernel that rejects the batch has the approval compensated; a redelivered message is a no-op; and one trace
/// follows an approval across the saga.
/// </summary>
public sealed class OccupancyApprovalSagaEndToEndTests
{
    private const string Requested = IntegrationEventTypes.AssetMutationBatchRequestedV1;
    private const string Applied = IntegrationEventTypes.AssetMutationBatchAppliedV1;
    private const string Rejected = IntegrationEventTypes.AssetMutationBatchRejectedV1;

    // ── Consumer down ─────────────────────────────────────────────────────────

    [Fact]
    public async Task KernelConsumerDownDuringApproval_OccupancyStaysPending_CatalogAndMapStayUnchanged_AndOnRecoveryTheBatchIsAppliedExactlyOnce()
    {
        await using var host = await ApprovalSagaHost.StartAsync();
        host.Fault.Mode = KernelFault.DownBeforeApply;
        await host.StageAsync(ApprovalSagaHost.Add(1));
        (await host.MapPoleAsync()).Properties.CustomAttributes.Should().NotContainKey(OccupancyAssetAttributes.ClassKey);

        var result = await host.ApproveAsync();

        result.AwaitingApply.Should().BeTrue();
        (await host.RelayAsync()).Should().Be(0, "the kernel's consumer is down");
        await host.ExpireBackoffAsync();
        (await host.RelayAsync()).Should().Be(0, "still down");

        var request = (await host.OutboxAsync(Requested)).Should().ContainSingle().Subject;
        request.Status.Should().Be(OutboxMessageStatus.Pending, "a failed delivery is retried, not given up on");
        request.AttemptCount.Should().Be(2);
        request.LastError.Should().Contain("unreachable");
        host.Fault.Calls.Should().Be(2);
        host.Fault.Applies.Should().Be(0);
        await AssertStillPendingAsync(host);
        (await host.AppliedBatchesAsync()).Should().Be(0);
        (await host.CatalogPoleAsync())!.Properties.CustomAttributes.Should().NotContainKey(OccupancyAssetAttributes.ClassKey);
        (await host.MapPoleAsync()).Properties.CustomAttributes.Should().NotContainKey(OccupancyAssetAttributes.ClassKey, "the map shows the mutation only once the kernel applied it");
        (await host.OutboxAsync(Applied)).Should().BeEmpty();
        (await host.OutboxAsync(Rejected)).Should().BeEmpty("a consumer that is down rejects nothing");
        (await host.InboxRecordsAsync(AssetMutationBatchApplyHandler.ConsumerName)).Should().Be(0);

        host.Fault.Mode = KernelFault.None;
        await host.ExpireBackoffAsync();
        (await host.RelayAsync()).Should().Be(1, "the redelivered batch reaches the recovered kernel");

        host.Fault.Applies.Should().Be(1);
        (await host.AppliedBatchesAsync()).Should().Be(1, "applied exactly once");
        var applied = await host.MapPoleAsync();
        applied.Properties.CustomAttributes.Should().Contain(OccupancyAssetAttributes.ClassKey, "Low");
        (await host.SagaAsync())!.State.Should().Be(OccupancyApplySagaState.Pending, "the kernel's answer has not reached the saga yet");
        (await host.RelayAsync()).Should().Be(1, "the Applied answer is delivered");

        (await host.SagaAsync())!.State.Should().Be(OccupancyApplySagaState.Committed);
        var order = await host.OrderAsync();
        order.Status.Should().Be(SharedUseOrderTypes.ApprovedStateKey);
        order.Attributes.Should().Contain(OccupancyApplySagaAttributes.StateKey, OccupancyApplySagaAttributes.Applied);
        (await host.SetsAsync()).Should().OnlyContain(set => set.Status == OccupancyChangeSetStatus.Committed);

        // The relay hands the very same batch over once more: nothing is applied or answered again.
        await host.MarkPendingAgainAsync(Requested);
        (await host.RelayAsync()).Should().Be(1);
        host.Fault.Calls.Should().Be(3, "the consumer's inbox recognized the batch before it reached the applier");
        (await host.AppliedBatchesAsync()).Should().Be(1);
        (await host.OutboxAsync(Applied)).Should().ContainSingle();
        (await host.CatalogPoleAsync())!.Properties.UpdatedAt.Should().Be(applied.Properties.UpdatedAt);
    }

    [Fact]
    public async Task KernelCrashingBetweenApplyAndReply_OnRedelivery_ReEmitsTheReply_AndTheCatalogIsNotAppliedAgain()
    {
        await using var host = await ApprovalSagaHost.StartAsync();
        host.Fault.Mode = KernelFault.CrashAfterApplyOnce;
        await host.StageAsync(ApprovalSagaHost.Add(1));
        await host.ApproveAsync();

        (await host.RelayAsync()).Should().Be(0, "the kernel died before it answered");

        host.Fault.Applies.Should().Be(1);
        (await host.AppliedBatchesAsync()).Should().Be(1, "the batch was applied before the crash");
        var appliedOnce = (await host.MapPoleAsync()).Properties;
        appliedOnce.CustomAttributes.Should().Contain(OccupancyAssetAttributes.ClassKey, "Low");
        (await host.OutboxAsync(Applied)).Should().BeEmpty("the reply was not staged");
        (await host.InboxRecordsAsync(AssetMutationBatchApplyHandler.ConsumerName)).Should().Be(0, "the inbox record and the reply are staged in one unit: neither was");
        (await host.SagaAsync())!.State.Should().Be(OccupancyApplySagaState.Pending);

        await host.ExpireBackoffAsync();
        (await host.RelayAsync()).Should().Be(1, "the redelivered batch is answered now");

        host.Fault.Applies.Should().Be(2, "the redelivery reached the applier, which answered from what it had recorded");
        (await host.AppliedBatchesAsync()).Should().Be(1, "the catalog was not applied again");
        (await host.CatalogPoleAsync())!.Properties.UpdatedAt.Should().Be(appliedOnce.UpdatedAt);
        (await host.OutboxAsync(Applied)).Should().ContainSingle("the answer is emitted once");
        (await host.RelayAsync()).Should().Be(1);
        (await host.SagaAsync())!.State.Should().Be(OccupancyApplySagaState.Committed);
        (await host.OrderAsync()).Status.Should().Be(SharedUseOrderTypes.ApprovedStateKey);
    }

    // ── Rejection ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task KernelRejectingTheBatch_CompensatesTheApproval_RestoresThePole_AndANewApprovalGetsAnAttemptTwoBatch()
    {
        await using var host = await ApprovalSagaHost.StartAsync(poleInCatalog: false);
        var baseline = new PoleOccupancyEntryBuilder().ForPole(ApprovalSagaHost.OwnersPole).OwnedBy(ApprovalSagaHost.Owner).OnSlot(1)
            .OccupiedBy(ApprovalSagaHost.Occupant).Build();
        await host.OccupyAsync(baseline);
        await host.StageAsync(ApprovalSagaHost.Add(2));
        await host.ApproveAsync();
        (await host.OccupancyAsync())!.Entries.Should().HaveCount(2, "the approval wrote the pole, pending the catalog");

        (await host.RelayAsync()).Should().Be(1, "the rejection is an answer: delivered, not retried");

        var request = (await host.OutboxAsync(Requested)).Should().ContainSingle().Subject;
        request.Status.Should().Be(OutboxMessageStatus.Dispatched);
        request.AttemptCount.Should().Be(0);
        (await host.AppliedBatchesAsync()).Should().Be(0);
        var rejection = await host.StagedRejectionAsync();
        rejection.ReasonCode.Should().Be(AssetMutationBatchRejectionCodes.FeatureNotFound);
        rejection.ConflictingFeatureIds.Should().Equal(ApprovalSagaHost.OwnersPole);
        rejection.SourceVerticalId.Should().Be(OccupancyApplySagaService.VerticalId);
        rejection.Reason.Should().Contain(ApprovalSagaHost.OwnersPole);
        (await host.InboxRecordsAsync(AssetMutationBatchApplyHandler.ConsumerName)).Should().Be(1);

        (await host.RelayAsync()).Should().Be(1, "the rejection reaches the saga");

        var saga = (await host.SagaAsync())!;
        saga.State.Should().Be(OccupancyApplySagaState.Reverted);
        saga.ReasonCode.Should().Be(AssetMutationBatchRejectionCodes.FeatureNotFound);
        saga.ConflictingFeatureIds.Should().Equal(ApprovalSagaHost.OwnersPole);
        var restored = (await host.OccupancyAsync())!;
        restored.Version.Should().Be(3, "the compensation is the next version after the approval's");
        restored.Entries.Should().BeEquivalentTo([baseline], "the pole holds what it held before the approval");
        (await host.SetsAsync()).Should().OnlyContain(set => set.Status == OccupancyChangeSetStatus.Reverted);
        var order = await host.OrderAsync();
        order.Status.Should().Be(ServiceOrderStatus.Completed);
        order.Attributes.Should().Contain(OccupancyApplySagaAttributes.StateKey, OccupancyApplySagaAttributes.Reverted);
        order.ActionLog.Select(entry => entry.Action).Should().Equal(OrderActionType.Approve, OccupancyApplyActions.Reverted);
        order.ActionLog[1].Comment.Should().Contain(AssetMutationBatchRejectionCodes.FeatureNotFound).And.Contain(ApprovalSagaHost.OwnersPole);

        await host.StageAsync(ApprovalSagaHost.Add(3));
        var second = await host.ApproveAsync();

        second.AssetMutationBatchId.Should().Be(OccupancyCommitService.AssetMutationBatchId(host.Order, attempt: 2));
        (await host.SagaAsync())!.Attempt.Should().Be(2);
        (await host.OutboxAsync(Requested)).Should().HaveCount(2);
    }

    // ── Rejection after a later order rewrote a pole (XD01-392) ───────────────

    [Fact]
    public async Task KernelRejectionArrivingAfterALaterChangeRewroteAPole_CompensatesWhatItCan_KeepsTheLaterChange_RecordsTheReplyOnce_AndLeavesAwaitingApply()
    {
        await using var host = await ApprovalSagaHost.StartAsync(poleInCatalog: false, withSecondPole: true);
        var baseline = new PoleOccupancyEntryBuilder().ForPole(ApprovalSagaHost.OwnersPole).OwnedBy(ApprovalSagaHost.Owner).OnSlot(1)
            .OccupiedBy(ApprovalSagaHost.Occupant).Build();
        await host.OccupyAsync(baseline);
        await host.StageAsync(ApprovalSagaHost.OwnersPole, ApprovalSagaHost.Add(2));
        await host.StageAsync(ApprovalSagaHost.SecondPole, ApprovalSagaHost.Add(1));
        await host.ApproveAsync();
        (await host.OccupancyAsync(ApprovalSagaHost.SecondPole))!.Entries.Should().ContainSingle("the approval wrote the second pole, pending the catalog");
        var approved = (await host.OccupancyAsync())!;

        // Another order's commit rewrites the shared pole before the kernel's answer arrives.
        var later = new PoleOccupancyEntryBuilder().ForPole(ApprovalSagaHost.OwnersPole).OwnedBy(ApprovalSagaHost.Owner).OnSlot(3)
            .OccupiedBy(ApprovalSagaHost.Other).Build();
        var rewritten = await host.OccupyAsync(ApprovalSagaHost.OwnersPole, [.. approved.Entries, later]);
        rewritten.Version.Should().BeGreaterThan(approved.Version);

        (await host.RelayAsync()).Should().Be(1, "the kernel rejects the batch: an answer, delivered");
        (await host.OutboxAsync(Rejected)).Should().ContainSingle();

        var delivered = await host.RelayAsync();

        delivered.Should().Be(1, "the saga handles the rejection without throwing out of the handler");
        (await host.InboxRecordsAsync(OccupancyApplySagaService.RejectedConsumerName)).Should().Be(1, "the reply is recorded: it is not redelivered forever");
        var saga = (await host.SagaAsync())!;
        saga.State.Should().Be(OccupancyApplySagaState.Reverted);
        saga.UnrestoredFeatureIds.Should().Equal(ApprovalSagaHost.OwnersPole);
        var shared = (await host.OccupancyAsync())!;
        shared.Version.Should().Be(rewritten.Version, "the compensation did not touch the pole the later change wrote");
        shared.Entries.Should().BeEquivalentTo(rewritten.Entries, "the later change's occupancy is kept");
        var second = (await host.OccupancyAsync(ApprovalSagaHost.SecondPole))!;
        second.Entries.Should().BeEmpty("the pole nobody else wrote is back at its baseline");
        (await host.SetsAsync()).Should().HaveCount(2).And.OnlyContain(set => set.Status == OccupancyChangeSetStatus.Reverted);
        var order = await host.OrderAsync();
        order.Status.Should().Be(ServiceOrderStatus.Completed, "the order stays at its status before the approval");
        OccupancyApplySagaAttributes.IsAwaitingApply(order.Attributes).Should().BeFalse();
        order.Attributes.Should().Contain(OccupancyApplySagaAttributes.StateKey, OccupancyApplySagaAttributes.Reverted)
            .And.Contain(OccupancyApplySagaAttributes.UnrestoredKey, ApprovalSagaHost.OwnersPole);
        order.ActionLog.Select(entry => entry.Action).Should().Equal(OrderActionType.Approve, OccupancyApplyActions.Reverted);
        order.ActionLog[1].Comment.Should().Contain($"compensated except on pole(s) {ApprovalSagaHost.OwnersPole}")
            .And.Contain("their occupancy was kept");
        order.ActionLog[1].Comment.Should().NotContain(ApprovalSagaHost.SecondPole + ":", "only the unrestored pole is named as kept");

        // The broker hands the very same reply over again: a no-op.
        var logged = order.ActionLog.Count;
        await host.MarkPendingAgainAsync(Rejected);
        (await host.RelayAsync()).Should().Be(1);
        (await host.InboxRecordsAsync(OccupancyApplySagaService.RejectedConsumerName)).Should().Be(1);
        (await host.SagaAsync())!.CompletedAt.Should().Be(saga.CompletedAt);
        (await host.OccupancyAsync())!.Version.Should().Be(rewritten.Version);
        (await host.OccupancyAsync(ApprovalSagaHost.SecondPole))!.Version.Should().Be(second.Version);
        (await host.OrderAsync()).ActionLog.Should().HaveCount(logged);

        // The transition guard no longer blocks the order: it can be staged and approved again.
        await host.StageAsync(ApprovalSagaHost.OwnersPole, ApprovalSagaHost.Add(4));
        var again = await host.ApproveAsync();

        again.AssetMutationBatchId.Should().Be(OccupancyCommitService.AssetMutationBatchId(host.Order, attempt: 2));
        (await host.SagaAsync())!.Attempt.Should().Be(2);
        var reapproved = await host.OrderAsync();
        OccupancyApplySagaAttributes.IsAwaitingApply(reapproved.Attributes).Should().BeTrue();
        reapproved.Attributes.Should().NotContainKey(OccupancyApplySagaAttributes.UnrestoredKey);
    }

    // ── Retryable is not a rejection ──────────────────────────────────────────

    [Fact]
    public async Task ABatchAddressedToAnotherCatalogInstance_IsNeitherAppliedNorRejected_RetriedWithBackoff_ThenLeftFailed_AndTheSagaStaysPending()
    {
        await using var host = await ApprovalSagaHost.StartAsync(staleCatalogInstanceId: "elsewhere", maxAttempts: 2);
        await host.StageAsync(ApprovalSagaHost.Add(1));
        await host.ApproveAsync();

        (await host.RelayAsync()).Should().Be(0);
        var afterFirst = (await host.OutboxAsync(Requested)).Single();
        afterFirst.Status.Should().Be(OutboxMessageStatus.Pending, "a failed attempt is retried");
        afterFirst.AttemptCount.Should().Be(1);
        afterFirst.LastError.Should().Contain("elsewhere");

        await host.ExpireBackoffAsync();
        (await host.RelayAsync()).Should().Be(0);

        var afterLast = (await host.OutboxAsync(Requested)).Single();
        afterLast.Status.Should().Be(OutboxMessageStatus.Failed, "after the last attempt the batch stays in the outbox as failed");
        afterLast.AttemptCount.Should().Be(2);
        (await host.OutboxAsync(Rejected)).Should().BeEmpty("a misaddressed batch is not this kernel's to reject");
        (await host.OutboxAsync(Applied)).Should().BeEmpty();
        (await host.InboxRecordsAsync(AssetMutationBatchApplyHandler.ConsumerName)).Should().Be(0);
        host.Fault.Calls.Should().Be(0, "it never reached the applier");
        await AssertStillPendingAsync(host);
    }

    // ── Duplicate delivery ────────────────────────────────────────────────────

    [Fact]
    public async Task TheKernelsAnswerDeliveredTwice_ChangesNothingTheSecondTime()
    {
        await using var host = await ApprovalSagaHost.StartAsync();
        await host.StageAsync(ApprovalSagaHost.Add(1));
        await host.ApproveAsync();
        (await host.RelayAsync()).Should().Be(1);
        (await host.RelayAsync()).Should().Be(1);
        var completedAt = (await host.SagaAsync())!.CompletedAt;
        var order = await host.OrderAsync();

        await host.MarkPendingAgainAsync(Applied);
        (await host.RelayAsync()).Should().Be(1);

        (await host.SagaAsync())!.CompletedAt.Should().Be(completedAt);
        var after = await host.OrderAsync();
        after.Status.Should().Be(SharedUseOrderTypes.ApprovedStateKey);
        after.ActionLog.Should().HaveCount(order.ActionLog.Count, "the second delivery logged nothing");
        (await host.InboxRecordsAsync(OccupancyApplySagaService.AppliedConsumerName)).Should().Be(1);
        (await host.AppliedBatchesAsync()).Should().Be(1);
    }

    // ── One trace per approval ────────────────────────────────────────────────

    [Fact]
    public async Task OneTraceFollowsAnApprovalAcrossTheSaga_TheEnvelopesCarryItOn_AndARetryStartsANewTrace()
    {
        var spans = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name is GeoAssetsActivitySource.SourceName or "GeoAssets.Tests.ApprovalSaga",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity =>
            {
                lock (spans) spans.Add(activity);
            },
        };
        ActivitySource.AddActivityListener(listener);
        using var requests = new ActivitySource("GeoAssets.Tests.ApprovalSaga");
        await using var host = await ApprovalSagaHost.StartAsync();
        host.Fault.Mode = KernelFault.DownBeforeApply;
        await host.StageAsync(ApprovalSagaHost.Add(1));

        ActivityTraceId approvalTrace;
        using (var request = requests.StartActivity("approval request"))
        {
            approvalTrace = request!.TraceId;
            await host.ApproveAsync();
        }

        (await host.RelayAsync()).Should().Be(0, "the kernel is down: the batch stays on the outbox");
        ActivityTraceId retryTrace;
        using (var request = requests.StartActivity("retry request"))
        {
            retryTrace = request!.TraceId;
            await host.RetryAsync();
        }

        retryTrace.Should().NotBe(approvalTrace, "an operator's retry is a request of its own");
        host.Fault.Mode = KernelFault.None;
        await host.ExpireBackoffAsync();
        (await host.RelayAsync()).Should().Be(2, "the original batch and its retry are delivered");
        (await host.RelayAsync()).Should().Be(2, "and both answers");

        var orderSpans = Saga(spans, host.Order);
        var applySpans = orderSpans.Where(span => string.Equals(span.OperationName, "Saga.AssetMutationApply", StringComparison.Ordinal)).ToList();
        applySpans.Select(span => span.TraceId).Should().BeEquivalentTo(
            [approvalTrace, approvalTrace, retryTrace], "the failed attempt and the redelivery of the original are steps of the approval's trace; the retry's, of its own");
        orderSpans.Where(span => string.Equals(span.OperationName, "Saga.AssetMutationApplied", StringComparison.Ordinal)).Select(span => span.TraceId)
            .Should().BeEquivalentTo([approvalTrace, retryTrace], "so is each answer");
        orderSpans.Single(span => string.Equals(span.OperationName, "Saga.AssetMutationRetry", StringComparison.Ordinal)).TraceId.Should().Be(retryTrace);

        var staged = await host.OutboxAsync(Requested);
        staged.Should().HaveCount(2);
        staged[0].TraceParent.Should().Contain(approvalTrace.ToString(), "the envelope the approval staged carries its trace");
        staged[1].TraceParent.Should().Contain(retryTrace.ToString(), "the retry's envelope carries the retry's");
        var replies = await host.OutboxAsync(Applied);
        replies.Should().HaveCount(2);
        replies.Select(reply => reply.TraceParent).Should().Contain(parent => parent != null && parent.Contains(approvalTrace.ToString(), StringComparison.Ordinal))
            .And.Contain(parent => parent != null && parent.Contains(retryTrace.ToString(), StringComparison.Ordinal), "each reply carries the trace of the delivery it answers");

        // Within the approval's trace, the consume of the request and of its answer.
        List<Activity> inApproval;
        lock (spans) inApproval = [.. spans.Where(span => span.TraceId == approvalTrace)];
        inApproval.Where(span => string.Equals(span.OperationName, "in-process process", StringComparison.Ordinal))
            .Select(span => span.GetTagItem("geoassets.message.type") as string)
            .Should().Contain([Requested, Applied]);
        (await host.SagaAsync())!.State.Should().Be(OccupancyApplySagaState.Committed);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static List<Activity> Saga(List<Activity> spans, string orderId)
    {
        lock (spans)
        {
            return [.. spans.Where(span => span.OperationName.StartsWith("Saga.", StringComparison.Ordinal)
                && string.Equals(span.GetTagItem("order.id") as string, orderId, StringComparison.Ordinal))];
        }
    }

    /// <summary>The approval wrote the poles, and nothing final has happened: pending sets, a pending saga, an order awaiting apply.</summary>
    private static async Task AssertStillPendingAsync(ApprovalSagaHost host)
    {
        var saga = (await host.SagaAsync())!;
        saga.State.Should().Be(OccupancyApplySagaState.Pending);
        var order = await host.OrderAsync();
        order.Status.Should().Be(ServiceOrderStatus.Completed);
        OccupancyApplySagaAttributes.IsAwaitingApply(order.Attributes).Should().BeTrue();
        (await host.SetsAsync()).Should().OnlyContain(set => set.Status == OccupancyChangeSetStatus.Pending);
        (await host.OccupancyAsync())!.Entries.Should().NotBeEmpty("the live occupancy was written");
    }
}
