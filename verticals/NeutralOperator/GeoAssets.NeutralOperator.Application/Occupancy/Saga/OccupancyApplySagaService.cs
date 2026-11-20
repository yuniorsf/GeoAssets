using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Messaging;
using GeoAssets.Core.Messaging.Events;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;

namespace GeoAssets.NeutralOperator.Application.Occupancy.Saga;

/// <summary>
/// The NeutralOperator side of the approval saga (XD01-383, ADR-001 Phase 7): an approval writes the poles' occupancy as
/// <i>pending</i> and stages one asset mutation batch (<see cref="OccupancyCommitService"/>); the kernel applies it and
/// answers. This service finalizes or compensates on that answer, and re-stages the batch when an operator asks.
///
/// <para><b>Applied</b> — the saga and its change sets become committed, and the order, which stayed in the status the
/// approval found it in with <see cref="OccupancyApplySagaAttributes.AwaitingApply"/>, moves along its approval transition,
/// logged as <see cref="OccupancyApplyActions.Applied"/>. <b>Rejected</b> — every pole of the saga is restored to what it
/// held before the approval, the saga and its change sets become reverted, and the order stays where it is (the status it
/// had before the approval) with <see cref="OccupancyApplySagaAttributes.Reverted"/> and the kernel's reason logged as
/// <see cref="OccupancyApplyActions.Reverted"/>. A pole another order wrote since the approval is not restored — the later
/// change wins — and the saga still reverts, naming it in <see cref="OccupancyApplySaga.UnrestoredFeatureIds"/>, on the
/// order's <see cref="OccupancyApplySagaAttributes.UnrestoredKey"/> and in the logged entry (XD01-392).</para>
///
/// <para><b>Exactly-once effects.</b> Each reply's message id is recorded in the inbox under its consumer name in the same
/// unit of work as everything it changes, and a saga that is no longer pending is never changed again: a redelivered or
/// duplicated reply is a no-op. Replies run outside any request, scoped to the organization the reply names (the batch's,
/// the pole owner's); a reply of another vertical, or for a batch no saga here holds, is ignored.</para>
/// </summary>
public sealed class OccupancyApplySagaService : IOccupancyApplySagaService
{
    /// <summary>The vertical id this module's batches carry as <see cref="AssetMutationBatchRequestedEvent.SourceVerticalId"/>.</summary>
    public const string VerticalId = "neutral-operator";

    /// <summary>The inbox consumer name of the <see cref="IntegrationEventTypes.AssetMutationBatchAppliedV1"/> handler.</summary>
    public const string AppliedConsumerName = "neutral-operator.occupancy-apply-saga.applied";

    /// <summary>The inbox consumer name of the <see cref="IntegrationEventTypes.AssetMutationBatchRejectedV1"/> handler.</summary>
    public const string RejectedConsumerName = "neutral-operator.occupancy-apply-saga.rejected";

    /// <summary>The configuration key of the applied handler's broker overrides.</summary>
    public const string AppliedConsumerKey = "OccupancyApplySagaApplied";

    /// <summary>The configuration key of the rejected handler's broker overrides.</summary>
    public const string RejectedConsumerKey = "OccupancyApplySagaRejected";

    /// <summary>How long a processed reply stays in the inbox.</summary>
    public static readonly TimeSpan InboxRetention = TimeSpan.FromDays(7);

    private readonly IServiceOrderRepository _orders;
    private readonly OrderTypeRegistry _orderTypes;
    private readonly ServiceOrderRules _rules;
    private readonly IOccupancyApplySagaStore _sagas;
    private readonly IOutboxWriter _outbox;
    private readonly IIdempotentInbox _inbox;
    private readonly IWorkflowUnitOfWork _unitOfWork;
    private readonly ITenantOrganizationScope _organizationScope;
    private readonly TimeProvider _timeProvider;

    public OccupancyApplySagaService(
        IServiceOrderRepository orders,
        OrderTypeRegistry orderTypes,
        ServiceOrderRules rules,
        IOccupancyApplySagaStore sagas,
        IOutboxWriter outbox,
        IIdempotentInbox inbox,
        IWorkflowUnitOfWork unitOfWork,
        ITenantOrganizationScope organizationScope,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(orders);
        ArgumentNullException.ThrowIfNull(orderTypes);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(sagas);
        ArgumentNullException.ThrowIfNull(outbox);
        ArgumentNullException.ThrowIfNull(inbox);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(organizationScope);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _orders = orders;
        _orderTypes = orderTypes;
        _rules = rules;
        _sagas = sagas;
        _outbox = outbox;
        _inbox = inbox;
        _unitOfWork = unitOfWork;
        _organizationScope = organizationScope;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// The id of the asset mutation batch the <paramref name="attempt"/>-th approval of an order stages: derived from the
    /// order id and the attempt, so a relayed batch is applied once by the kernel, and the approval after a rejected one is
    /// a new batch. The first attempt keeps <see cref="OccupancyCommitService.AssetMutationBatchId(string)"/>'s id.
    /// </summary>
    public static Guid BatchId(string orderId, int attempt) => OccupancyCommitService.AssetMutationBatchId(orderId, attempt);

    public Task<OccupancyApplySagaResult> HandleAppliedAsync(AssetBatchAppliedEvent applied, IntegrationMessageContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(applied);
        ArgumentNullException.ThrowIfNull(context);

        return ReplyAsync(applied.BatchId, applied.OrganizationId, applied.SourceVerticalId, context, AppliedConsumerName, async (saga, now, token) =>
        {
            switch (saga.State)
            {
                case OccupancyApplySagaState.Committed:
                    return OccupancyApplySagaResult.Duplicate;
                case OccupancyApplySagaState.Reverted:
                    return new OccupancyApplySagaResult(OccupancyApplySagaOutcome.AppliedAfterRevert, saga);
            }

            var committed = saga with { State = OccupancyApplySagaState.Committed, CompletedAt = now };
            if (!(await _sagas.CompleteAsync(committed, token).ConfigureAwait(false)).Completed)
                return OccupancyApplySagaResult.Duplicate;

            await SettleOrderAsync(
                saga,
                OccupancyApplySagaAttributes.Applied,
                saga.ApprovedStatus,
                new OrderActionLog(
                    OccupancyApplyActions.Applied, saga.RequestedBy, now.UtcDateTime,
                    $"The asset catalog applied batch {saga.BatchId}: the occupancy approved on {saga.Batch.Mutations.Count} pole(s) is committed.",
                    saga.ApprovedStatus)
                {
                    ActorKind = ActorKind.System,
                },
                now,
                unrestored: null,
                token).ConfigureAwait(false);

            return new OccupancyApplySagaResult(OccupancyApplySagaOutcome.Committed, committed);
        }, ct);
    }

    public Task<OccupancyApplySagaResult> HandleRejectedAsync(AssetMutationBatchRejectedEvent rejected, IntegrationMessageContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(rejected);
        ArgumentNullException.ThrowIfNull(context);

        return ReplyAsync(rejected.BatchId, rejected.OrganizationId, rejected.SourceVerticalId, context, RejectedConsumerName, async (saga, now, token) =>
        {
            if (saga.State != OccupancyApplySagaState.Pending)
                return OccupancyApplySagaResult.Duplicate;

            var reason = Truncate(rejected.Reason, OccupancyApplySaga.MaxReasonLength);
            var reverted = saga with
            {
                State = OccupancyApplySagaState.Reverted,
                CompletedAt = now,
                ReasonCode = Truncate(rejected.ReasonCode, OccupancyApplySaga.MaxReasonCodeLength),
                Reason = reason,
                ConflictingFeatureIds = [.. rejected.ConflictingFeatureIds],
            };
            var completion = await _sagas.CompleteAsync(reverted, token).ConfigureAwait(false);
            if (!completion.Completed)
                return OccupancyApplySagaResult.Duplicate;
            reverted = reverted with { UnrestoredFeatureIds = completion.UnrestoredFeatureIds };

            var conflicting = rejected.ConflictingFeatureIds.Count == 0
                ? string.Empty
                : $" Conflicting feature(s): {string.Join(", ", rejected.ConflictingFeatureIds)}.";
            var compensated = reverted.UnrestoredFeatureIds.Count == 0
                ? $"The approval was compensated: the occupancy of {saga.Batch.Mutations.Count} pole(s) was restored and the changes must be staged again."
                : $"The approval was compensated except on pole(s) {string.Join(", ", reverted.UnrestoredFeatureIds)}: a later change superseded the approval there, so their occupancy was kept. The occupancy of the other pole(s) was restored and the changes must be staged again.";
            await SettleOrderAsync(
                saga,
                OccupancyApplySagaAttributes.Reverted,
                status: null,
                new OrderActionLog(
                    OccupancyApplyActions.Reverted, saga.RequestedBy, now.UtcDateTime,
                    $"The asset catalog rejected batch {saga.BatchId} ({rejected.ReasonCode}): {reason}{conflicting} {compensated}")
                {
                    ActorKind = ActorKind.System,
                },
                now,
                reverted.UnrestoredFeatureIds,
                token).ConfigureAwait(false);

            return new OccupancyApplySagaResult(OccupancyApplySagaOutcome.Reverted, reverted);
        }, ct);
    }

    /// <summary>
    /// Re-stages the batch of the order's pending saga, unchanged — the same batch id, so the kernel applies it at most once
    /// and answers again — as a new message, restarting the saga's timeout. Authorized as the approval is: unknown order
    /// (<see cref="KeyNotFoundException"/>); <see cref="ServiceOrderRules"/> for <see cref="OrderAction.Approve"/> (a caller
    /// outside the organization gate is refused there, which answers as an unknown order); owner only. Then
    /// <see cref="OccupancyApplyNotPendingException"/> when the order has no pending saga.
    /// </summary>
    public async Task<OccupancyApplySagaView> RetryAsync(string orderId, WorkflowPrincipal principal, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
        ArgumentNullException.ThrowIfNull(principal);
        ct.ThrowIfCancellationRequested();

        var order = await _orders.GetByIdAsync(orderId, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Service order '{orderId}' not found.");
        var verdict = _rules.Evaluate(principal, OrderAction.Approve, order);
        if (!verdict.Allowed)
            throw new OrderActionDeniedException(orderId, verdict);
        if (!Guid.TryParse(principal.OrganizationId, out var organizationId) || organizationId != order.OrganizationId)
            throw new OrderActionDeniedException(orderId, RuleEvaluationResult.Deny(OrderAction.Approve, OccupancyCommitService.OwnerOnlyReason));

        var saga = await _sagas.FindLatestForOrderAsync(order.Id, ct).ConfigureAwait(false);
        if (saga is not { State: OccupancyApplySagaState.Pending })
            throw new OccupancyApplyNotPendingException(order.Id, saga?.State);

        var now = _timeProvider.GetUtcNow();
        var retry = saga.RetryCount + 1;
        await _unitOfWork.ExecuteAsync(
            async token =>
            {
                if (!await _sagas.RecordRetryAsync(saga.BatchId, now, token).ConfigureAwait(false))
                    throw new OccupancyApplyNotPendingException(order.Id, state: null);

                await _outbox.EnqueueAsync(
                    saga.Batch,
                    IntegrationEventTypes.AssetMutationBatchRequestedV1,
                    partitionKey: order.Id,
                    catalogInstanceId: saga.Batch.CatalogInstanceId,
                    correlationId: order.Id,
                    causationId: $"{OccupancyCommitService.CausationId(order.Id)}:retry:{saga.BatchId:D}:{retry}",
                    ct: token).ConfigureAwait(false);
                await _orders.AppendActionAsync(
                    order.Id,
                    new OrderActionLog(
                        OccupancyApplyActions.Retried, principal.UserId, now.UtcDateTime,
                        $"Batch {saga.BatchId} staged again for the asset catalog (retry {retry}).")
                    {
                        ActorKind = principal.Kind,
                    },
                    token).ConfigureAwait(false);
                return true;
            },
            ct).ConfigureAwait(false);

        return OccupancyApplySagaView.From(saga with { RetryCount = retry, LastRequestedAt = now, TimedOutAt = null });
    }

    /// <summary>
    /// Runs one reply: ignored unless it is this vertical's and names an organization; then, in one unit of work scoped to
    /// that organization, the inbox record under <paramref name="consumerName"/> (a duplicate stops here), the saga lookup (a
    /// batch no saga holds is ignored) and <paramref name="apply"/>.
    /// </summary>
    private async Task<OccupancyApplySagaResult> ReplyAsync(
        Guid batchId,
        Guid organizationId,
        string? sourceVerticalId,
        IntegrationMessageContext context,
        string consumerName,
        Func<OccupancyApplySaga, DateTimeOffset, CancellationToken, Task<OccupancyApplySagaResult>> apply,
        CancellationToken ct)
    {
        if (sourceVerticalId is not null && !string.Equals(sourceVerticalId, VerticalId, StringComparison.Ordinal))
            return OccupancyApplySagaResult.Ignored;
        if (organizationId == Guid.Empty)
            return OccupancyApplySagaResult.Ignored;

        _organizationScope.Enter(organizationId);
        return await _unitOfWork.ExecuteAsync(
            async token =>
            {
                if (!await _inbox.TryRecordProcessedAsync(context.MessageId, consumerName, context.MessageType, InboxRetention, token).ConfigureAwait(false))
                    return OccupancyApplySagaResult.Duplicate;

                var saga = await _sagas.FindAsync(batchId, token).ConfigureAwait(false);
                return saga is null
                    ? OccupancyApplySagaResult.Ignored
                    : await apply(saga, _timeProvider.GetUtcNow(), token).ConfigureAwait(false);
            },
            ct).ConfigureAwait(false);
    }

    /// <summary>
    /// The order's side of a reply: its saga state attribute set to <paramref name="state"/> and, when
    /// <paramref name="status"/> is given and the order is still where the approval left it, moved there; then
    /// <paramref name="entry"/> logged (its resulting status only when the order did move). <paramref name="unrestored"/>
    /// names the poles a revert left untouched, on the order's <see cref="OccupancyApplySagaAttributes.UnrestoredKey"/>.
    /// </summary>
    private async Task SettleOrderAsync(
        OccupancyApplySaga saga,
        string state,
        string? status,
        OrderActionLog entry,
        DateTimeOffset now,
        IReadOnlyList<string>? unrestored,
        CancellationToken ct)
    {
        if (await _orders.GetByIdAsync(saga.OrderId, ct).ConfigureAwait(false) is not ServiceOrder order)
            return;

        var moves = status is not null && string.Equals(order.Status, saga.PreviousStatus, StringComparison.Ordinal);
        await _orders.UpdateAsync(
            OccupancyOrderRevision.With(
                order,
                _orderTypes.Find(order.OrderTypeId),
                moves ? status! : order.Status,
                OccupancyOrderRevision.SagaAttributes(order, state, unrestored: unrestored),
                now),
            ct).ConfigureAwait(false);
        await _orders.AppendActionAsync(order.Id, moves ? entry : entry with { ResultingStatus = null }, ct).ConfigureAwait(false);
    }

    private static string Truncate(string value, int max) =>
        string.IsNullOrEmpty(value) || value.Length <= max ? value ?? string.Empty : value[..max];
}
