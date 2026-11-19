using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Messaging;
using GeoAssets.Core.Messaging.Events;
using GeoAssets.Core.Models;
using GeoAssets.Workflow.Notifications;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Registry;

namespace GeoAssets.Workflow.Targets;

/// <summary>
/// The global target ledger's operations for orders the kernel does not hold (XD01-379, ADR-001 §3.4): a remote
/// vertical reserves, releases and commits its orders' targets here, and the kernel releases them when the vertical
/// reports an order terminal. Selection, eligibility, limits and conflict evaluation are
/// <see cref="OrderTargetSelectionService"/>'s and <see cref="TargetConflictEvaluator"/>'s, searched across every
/// vertical; every change is written with its integration event (<see cref="TargetLedgerEvent"/>) in one
/// <see cref="IWorkflowUnitOfWork"/>, so the outbox never reports a change that was not stored, nor misses one that was.
///
/// Authorization is the caller's: the ledger routes admit only service callers, and every operation here is scoped to
/// the organization the caller names — a ledger order holding targets of another organization reads as unknown.
/// </summary>
public sealed class TargetLedgerService
{
    /// <summary>The inbox consumer name under which <see cref="ReleaseTerminalAsync"/> records the events it applied.</summary>
    public const string TerminalReleaseConsumerName = "kernel.target-ledger.terminal-release";

    /// <summary>How long a processed <c>OrderStateChangedV1</c> stays in the inbox for deduplication.</summary>
    public static readonly TimeSpan InboxRetention = TimeSpan.FromDays(7);

    private readonly OrderTargetSelectionService _selection;
    private readonly ITargetLedgerStore _store;
    private readonly IWorkflowUnitOfWork _unitOfWork;
    private readonly IOutboxWriter _outbox;
    private readonly IIdempotentInbox _inbox;
    private readonly IOrderTypeDescriptorRegistry _descriptors;
    private readonly TimeProvider _timeProvider;
    private readonly ITenantOrganizationScope _organizationScope;

    public TargetLedgerService(
        OrderTargetSelectionService selection,
        ITargetLedgerStore store,
        IWorkflowUnitOfWork unitOfWork,
        IOutboxWriter outbox,
        IIdempotentInbox inbox,
        IOrderTypeDescriptorRegistry descriptors,
        TimeProvider timeProvider,
        ITenantOrganizationScope organizationScope)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(outbox);
        ArgumentNullException.ThrowIfNull(inbox);
        ArgumentNullException.ThrowIfNull(descriptors);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(organizationScope);

        _selection = selection;
        _store = store;
        _unitOfWork = unitOfWork;
        _outbox = outbox;
        _inbox = inbox;
        _descriptors = descriptors;
        _timeProvider = timeProvider;
        _organizationScope = organizationScope;
    }

    /// <summary>The descriptor <paramref name="verticalId"/> published for <paramref name="orderTypeId"/>, or null (ordinal).</summary>
    public OrderTypeDescriptor? FindOrderType(string verticalId, string orderTypeId)
    {
        ArgumentNullException.ThrowIfNull(verticalId);
        ArgumentNullException.ThrowIfNull(orderTypeId);

        return _descriptors.GetAll().FirstOrDefault(d =>
            string.Equals(d.VerticalId, verticalId, StringComparison.Ordinal)
            && string.Equals(d.OrderTypeId, orderTypeId, StringComparison.Ordinal));
    }

    /// <summary>
    /// Whether <paramref name="key"/> already holds targets of an organization other than
    /// <paramref name="organizationId"/> — an order the caller must not see, nor add to.
    /// </summary>
    public async Task<bool> BelongsToAnotherOrganizationAsync(TargetLedgerKey key, Guid organizationId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(key);
        _organizationScope.Enter(organizationId);

        var owner = await _store.FindOrganizationAsync(key, ct).ConfigureAwait(false);
        return owner is { } organization && organization != organizationId;
    }

    /// <summary>What reserving the selection would add; writes nothing.</summary>
    public Task<TargetSelectionPreview> PreviewAsync(
        TargetLedgerOrder order, TargetSelectionRequest request, string performedBy, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(order);
        _organizationScope.Enter(order.OrganizationId);
        return _selection.PreviewLedgerAsync(order, request, performedBy, _store, ct);
    }

    /// <summary>
    /// Reserves the selection for <paramref name="order"/> and stages <see cref="TargetsReservedEvent"/> with the targets
    /// really inserted, in one unit of work (no event when none was). Safe to repeat: features the order already holds
    /// are skipped.
    /// </summary>
    /// <exception cref="TargetLedgerConflictException">A kept feature is held, in any vertical, with blocking exclusivity; nothing is written.</exception>
    public async Task<TargetSelectionResult> ReserveAsync(
        TargetLedgerOrder order, TargetSelectionRequest request, string performedBy, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(order);
        _organizationScope.Enter(order.OrganizationId);

        var reservation = await _selection.PlanLedgerReservationAsync(order, request, performedBy, _store, ct).ConfigureAwait(false);
        if (reservation.Conflicts.Count > 0)
            throw new TargetLedgerConflictException(reservation.Conflicts);

        // Last exit before the first write: a caller that gave up leaves the ledger untouched.
        ct.ThrowIfCancellationRequested();

        var inserted = await _unitOfWork.ExecuteAsync(
            async token =>
            {
                // Under the feature locks, repeat the conflict check: a reservation committed by another vertical since
                // the plan above was read is now visible, so two concurrent exclusive reservations cannot both pass.
                var featureIds = reservation.Targets.Select(t => t.FeatureId).Distinct(StringComparer.Ordinal).ToList();
                await _store.LockFeaturesAsync(featureIds, token).ConfigureAwait(false);
                var usages = await _store.FindOpenUsagesAsync(featureIds, order.Key, token).ConfigureAwait(false);
                var conflicts = OrderTargetSelectionService.LedgerConflicts(order, featureIds, usages);
                if (conflicts.Count > 0)
                    throw new TargetLedgerConflictException(conflicts);

                var written = await _store.ReserveAsync(order, reservation.Targets, token).ConfigureAwait(false);
                var reserved = new TargetsReservedEvent
                {
                    VerticalId = order.VerticalId,
                    OrderId = order.OrderId,
                    OrganizationId = order.OrganizationId,
                    Targets = Items(written),
                    OccurredAt = _timeProvider.GetUtcNow(),
                    PerformedBy = performedBy,
                };
                await StageAsync(reserved, IntegrationEventTypes.TargetsReservedV1, token).ConfigureAwait(false);
                return written.Count;
            },
            ct).ConfigureAwait(false);

        return reservation.Result with { Added = inserted };
    }

    /// <summary>
    /// Releases <paramref name="key"/>'s open targets in <paramref name="organizationId"/> — those in
    /// <paramref name="targetIds"/>, or all when null — and stages <see cref="TargetsReleasedEvent"/> when any was.
    /// Returns null when the order holds no target in that organization (unknown, or another organization's);
    /// otherwise the targets released, empty when there was nothing left to release.
    /// </summary>
    public Task<IReadOnlyList<ServiceOrderTarget>?> ReleaseAsync(
        TargetLedgerKey key, Guid organizationId, IReadOnlyCollection<string>? targetIds, string? performedBy, CancellationToken ct) =>
        ChangeAsync(
            key,
            organizationId,
            token => _store.ReleaseAsync(key, organizationId, targetIds, _timeProvider.GetUtcNow(), token),
            changed => new TargetsReleasedEvent
            {
                VerticalId = key.VerticalId,
                OrderId = key.OrderId,
                OrganizationId = organizationId,
                Targets = Items(changed),
                OccurredAt = _timeProvider.GetUtcNow(),
                PerformedBy = performedBy,
            },
            IntegrationEventTypes.TargetsReleasedV1,
            ct);

    /// <summary>
    /// Commits <paramref name="key"/>'s open, unfinished targets in <paramref name="organizationId"/> as
    /// <see cref="OrderTargetStatus.Done"/> — those in <paramref name="targetIds"/>, or all when null — and stages
    /// <see cref="TargetsCommittedEvent"/> when any changed. Null and empty mean what they mean for <see cref="ReleaseAsync"/>.
    /// </summary>
    public Task<IReadOnlyList<ServiceOrderTarget>?> CommitAsync(
        TargetLedgerKey key, Guid organizationId, IReadOnlyCollection<string>? targetIds, string? performedBy, CancellationToken ct) =>
        ChangeAsync(
            key,
            organizationId,
            token => _store.CommitAsync(key, organizationId, targetIds, token),
            changed => new TargetsCommittedEvent
            {
                VerticalId = key.VerticalId,
                OrderId = key.OrderId,
                OrganizationId = organizationId,
                Targets = Items(changed),
                OccurredAt = _timeProvider.GetUtcNow(),
                PerformedBy = performedBy,
            },
            IntegrationEventTypes.TargetsCommittedV1,
            ct);

    /// <summary>One page of <paramref name="key"/>'s targets in <paramref name="organizationId"/>, released ones included.</summary>
    public Task<PagedResult<TargetLedgerEntry>> GetPageAsync(
        TargetLedgerKey key, Guid organizationId, int skip, int take, CancellationToken ct)
    {
        _organizationScope.Enter(organizationId);
        return _store.GetPageAsync(key, organizationId, skip, take, ct);
    }

    /// <summary>
    /// Applies a vertical's <c>OrderStateChangedV1</c>: when <paramref name="message"/> names a remote vertical and its
    /// new status is terminal, releases every open target of that order, once — the message id is recorded in the inbox
    /// under <see cref="TerminalReleaseConsumerName"/> in the same unit of work as the release and its
    /// <see cref="TargetsReleasedEvent"/>, so a redelivered message changes nothing. Kernel orders (no vertical, or
    /// <see cref="TargetLedger.KernelVerticalId"/>) are left to the in-process release that runs when the order's status is
    /// written. Terminal means <see cref="OrderStateChangedEvent.IsTerminal"/> when the vertical set it, otherwise one of
    /// the <see cref="OrderTypeDescriptor.TerminalStatuses"/> it published for the order type; when neither says so,
    /// nothing is released. Returns whether this call released anything.
    /// </summary>
    public async Task<bool> ReleaseTerminalAsync(OrderStateChangedEvent message, IntegrationMessageContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(context);

        if (!TargetLedger.IsRemoteVerticalId(message.VerticalId) || string.IsNullOrWhiteSpace(message.OrderId)) return false;
        if (!IsTerminal(message)) return false;

        // A remote vertical must say whose order it is: the release runs scoped to that organization (row-level security),
        // and an unscoped release would consume the inbox record while finding nothing, so a corrected redelivery would
        // then be dropped as a duplicate. Without it the message is left unconsumed.
        if (message.OrganizationId is not { } organizationId || organizationId == Guid.Empty) return false;
        _organizationScope.Enter(organizationId);

        var key = new TargetLedgerKey(message.VerticalId!, message.OrderId);
        return await _unitOfWork.ExecuteAsync(
            async token =>
            {
                if (!await _inbox.TryRecordProcessedAsync(context.MessageId, TerminalReleaseConsumerName, context.MessageType, InboxRetention, token).ConfigureAwait(false))
                    return false;

                var released = await _store.ReleaseAsync(key, organizationId, targetIds: null, _timeProvider.GetUtcNow(), token).ConfigureAwait(false);
                foreach (var group in released.GroupBy(t => t.OrganizationId))
                {
                    var releasedEvent = new TargetsReleasedEvent
                    {
                        VerticalId = key.VerticalId,
                        OrderId = key.OrderId,
                        OrganizationId = group.Key,
                        Targets = Items([.. group]),
                        OccurredAt = _timeProvider.GetUtcNow(),
                        PerformedBy = message.PerformedBy,
                    };
                    await StageAsync(releasedEvent, IntegrationEventTypes.TargetsReleasedV1, token).ConfigureAwait(false);
                }

                return released.Count > 0;
            },
            ct).ConfigureAwait(false);
    }

    private bool IsTerminal(OrderStateChangedEvent message) =>
        message.IsTerminal
        ?? FindOrderType(message.VerticalId!, message.OrderTypeId)?.TerminalStatuses.Contains(message.NewStatus, StringComparer.Ordinal)
        ?? false;

    private async Task<IReadOnlyList<ServiceOrderTarget>?> ChangeAsync(
        TargetLedgerKey key,
        Guid organizationId,
        Func<CancellationToken, Task<IReadOnlyList<ServiceOrderTarget>>> change,
        Func<IReadOnlyList<ServiceOrderTarget>, TargetLedgerEvent> toEvent,
        string messageType,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(key);
        _organizationScope.Enter(organizationId);

        var owner = await _store.FindOrganizationAsync(key, ct).ConfigureAwait(false);
        if (owner != organizationId) return null;

        return await _unitOfWork.ExecuteAsync(
            async token =>
            {
                var changed = await change(token).ConfigureAwait(false);
                if (changed.Count > 0)
                    await StageAsync(toEvent(changed), messageType, token).ConfigureAwait(false);

                return changed;
            },
            ct).ConfigureAwait(false);
    }

    /// <summary>Stages <paramref name="message"/> on the outbox, partitioned by order, unless it names no target.</summary>
    private async Task StageAsync(TargetLedgerEvent message, string messageType, CancellationToken ct)
    {
        if (message.Targets.Count == 0) return;

        await _outbox.EnqueueAsync(
            message,
            messageType,
            partitionKey: $"{message.VerticalId}/{message.OrderId}",
            ct: ct).ConfigureAwait(false);
    }

    private static List<TargetLedgerEventItem> Items(IReadOnlyList<ServiceOrderTarget> targets) =>
        [.. targets.Select(t => new TargetLedgerEventItem(t.Id, t.FeatureId))];
}
