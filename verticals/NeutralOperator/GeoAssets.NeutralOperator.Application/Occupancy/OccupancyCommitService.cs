using System.Security.Cryptography;
using System.Text;
using GeoAssets.Core.Interfaces;
using GeoAssets.NeutralOperator.Domain.Occupancy;
using GeoAssets.Core.Messaging;
using GeoAssets.Core.Messaging.Events;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using GeoAssets.Workflow.Targets;

namespace GeoAssets.NeutralOperator.Application.Occupancy;

/// <summary>
/// Approves an order and commits what was staged on it into the poles' live occupancy
/// (XD01-318), on ports only, with the writes in one <see cref="IWorkflowUnitOfWork"/>.
///
/// <para><b>Order of checks:</b> arguments; unknown order (<see cref="KeyNotFoundException"/>);
/// <see cref="ServiceOrderRules"/> for <see cref="OrderAction.Approve"/> (a caller outside the
/// organization gate is refused there); <b>owner only</b> — a caller the rules allowed whose
/// organization is not the order's (a grantee, a dispatch recipient) is refused too, because only
/// the pole owner writes its occupancy and the occupancy stores answer no other organization; the
/// order's type, its target policy and its approval transition. Only then are targets, staged
/// sets and occupancy read.</para>
///
/// <para><b>Staleness</b> follows the staging service: a set records the pole's version it was
/// staged against, and a set whose pole has moved on since is never applied over the newer
/// occupancy. Every stale pole is named in one <see cref="OccupancyConflictException"/>, before
/// anything is written; a pole that moves between that check and the write fails the write
/// itself, and the unit rolls back.</para>
///
/// <para><b>The write</b>, poles in ordinal order: each pole's live occupancy is replaced with
/// its set applied (over-capacity entries included) and the set is marked committed; sets on
/// skipped or not-executable targets are discarded; then the order moves along its approval
/// transition, carrying the row version it was read with, and the approval is logged — after the
/// sets are committed, because closing an order discards whatever is still staged on it; last,
/// one asset mutation batch with the written poles' occupancy attributes is staged on the
/// outbox.</para>
/// </summary>
public sealed class OccupancyCommitService : IOccupancyCommitService
{
    /// <summary>What the asset mutation batch names as the service that requested it.</summary>
    public const string RequestedByService = "GeoAssets.Workflow.OccupancyCommit";

    /// <summary>The reason a caller the rules allowed is refused when it is not of the order's organization.</summary>
    public const string OwnerOnlyReason =
        "Occupancy changes are committed only by members of the organization that owns the order's poles.";

    private readonly IServiceOrderRepository _orders;
    private readonly OrderTypeRegistry _orderTypes;
    private readonly ServiceOrderRules _rules;
    private readonly IServiceOrderTargetReader _targets;
    private readonly IPoleOccupancyReader _occupancy;
    private readonly IPoleOccupancyWriter _occupancyWriter;
    private readonly IOccupancyChangeSetReader _changeSets;
    private readonly IOccupancyChangeSetWriter _changeSetWriter;
    private readonly IOutboxWriter _outbox;
    private readonly IWorkflowUnitOfWork _unitOfWork;
    private readonly OrderOccupancyOptions _options;
    private readonly TimeProvider _timeProvider;

    public OccupancyCommitService(
        IServiceOrderRepository orders,
        OrderTypeRegistry orderTypes,
        ServiceOrderRules rules,
        IServiceOrderTargetReader targets,
        IPoleOccupancyReader occupancy,
        IPoleOccupancyWriter occupancyWriter,
        IOccupancyChangeSetReader changeSets,
        IOccupancyChangeSetWriter changeSetWriter,
        IOutboxWriter outbox,
        IWorkflowUnitOfWork unitOfWork,
        OrderOccupancyOptions options,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(orders);
        ArgumentNullException.ThrowIfNull(orderTypes);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(occupancy);
        ArgumentNullException.ThrowIfNull(occupancyWriter);
        ArgumentNullException.ThrowIfNull(changeSets);
        ArgumentNullException.ThrowIfNull(changeSetWriter);
        ArgumentNullException.ThrowIfNull(outbox);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _orders = orders;
        _orderTypes = orderTypes;
        _rules = rules;
        _targets = targets;
        _occupancy = occupancy;
        _occupancyWriter = occupancyWriter;
        _changeSets = changeSets;
        _changeSetWriter = changeSetWriter;
        _outbox = outbox;
        _unitOfWork = unitOfWork;
        _options = options;
        _timeProvider = timeProvider;
    }

    /// <summary>The causation id of the asset mutation batch an order's approval stages.</summary>
    public static string CausationId(string orderId) => $"occupancy-commit:{orderId}";

    /// <summary>
    /// The id of the asset mutation batch an order's approval stages: derived from the order id,
    /// so a relayed batch delivered twice is applied once by an idempotent consumer.
    /// </summary>
    public static Guid AssetMutationBatchId(string orderId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(CausationId(orderId)));
        return new Guid(hash.AsSpan(0, 16));
    }

    public async Task<OccupancyCommitResult> ApproveAndCommitAsync(string orderId, WorkflowPrincipal principal, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
        ArgumentNullException.ThrowIfNull(principal);
        ct.ThrowIfCancellationRequested();

        var order = await AuthorizeAsync(orderId, principal, ct).ConfigureAwait(false);
        var orderType = _orderTypes.Find(order.OrderTypeId)
            ?? throw new OrderTypeNotRegisteredException(order.OrderTypeId, $"Order type '{order.OrderTypeId}' is not registered.");
        if (!OccupancyApproval.Commits(orderType))
            throw new InvalidOperationException($"Order type '{orderType.Id}' declares no target policy, so its orders hold no staged occupancy.");
        var approvedStatus = OccupancyApproval.ApprovedStatus(orderType, order.Status)
            ?? throw new InvalidServiceOrderTransitionException(order.Status, order.Status);

        if (order is not ServiceOrder stored)
            throw new InvalidOperationException($"Expected a {nameof(ServiceOrder)} instance, got {order.GetType().Name}.");

        var plan = await PlanAsync(order.Id, ct).ConfigureAwait(false);
        var now = _timeProvider.GetUtcNow();

        // Last exit before the first write: a caller that gave up leaves the order unapproved.
        ct.ThrowIfCancellationRequested();

        var written = await _unitOfWork
            .ExecuteAsync(token => WriteAsync(stored, orderType, approvedStatus, plan, principal, now, token), ct)
            .ConfigureAwait(false);

        var changesByKind = plan.Commits
            .SelectMany(commit => commit.Set.Changes)
            .GroupBy(change => change.Kind)
            .OrderBy(group => group.Key)
            .ToDictionary(group => group.Key, group => group.Count());

        return new OccupancyCommitResult
        {
            OrderId = order.Id,
            OrderTypeId = order.OrderTypeId,
            PreviousStatus = order.Status,
            Status = approvedStatus,
            ChangeSetsCommitted = plan.Commits.Count,
            ChangeSetsDiscarded = plan.Discards.Count,
            ChangesCommitted = changesByKind.Values.Sum(),
            ChangesByKind = changesByKind,
            PolesUpdated = written.Count,
            AssetMutationBatchId = written.Count > 0 ? AssetMutationBatchId(order.Id) : null,
        };
    }

    // ── Read and decide ───────────────────────────────────────────────────────

    private async Task<IServiceOrder> AuthorizeAsync(string orderId, WorkflowPrincipal principal, CancellationToken ct)
    {
        var order = await _orders.GetByIdAsync(orderId, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Service order '{orderId}' not found.");

        var verdict = _rules.Evaluate(principal, OrderAction.Approve, order);
        if (!verdict.Allowed)
            throw new OrderActionDeniedException(orderId, verdict);

        if (!Guid.TryParse(principal.OrganizationId, out var organizationId) || organizationId != order.OrganizationId)
            throw new OrderActionDeniedException(orderId, RuleEvaluationResult.Deny(OrderAction.Approve, OwnerOnlyReason));

        return order;
    }

    /// <summary>
    /// Which staged sets are committed and which discarded, each committed set already applied
    /// to its pole's live occupancy. Refuses, naming every such pole, when a set was staged
    /// against a version of its pole that is no longer the live one.
    /// </summary>
    private async Task<CommitPlan> PlanAsync(string orderId, CancellationToken ct)
    {
        var targets = (await AllTargetsAsync(orderId, ct).ConfigureAwait(false))
            .ToDictionary(target => target.Id, StringComparer.Ordinal);
        var staged = await AllStagedAsync(orderId, ct).ConfigureAwait(false);

        var (toCommit, toDiscard) = Partition(staged, targets);

        var poleIds = toCommit.Select(set => set.PoleFeatureId).Distinct(StringComparer.Ordinal).ToList();
        var live = poleIds.Count == 0
            ? []
            : (await _occupancy.GetForPolesAsync(poleIds, ct).ConfigureAwait(false))
                .ToDictionary(pole => pole.PoleFeatureId, StringComparer.Ordinal);

        var stale = toCommit
            .Where(set => set.IsStaleAgainst(live.GetValueOrDefault(set.PoleFeatureId)?.Version ?? PoleOccupancy.UnsavedVersion))
            .Select(set => set.PoleFeatureId)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
        if (stale.Count > 0)
            throw new OccupancyConflictException(stale);

        var commits = toCommit
            .OrderBy(set => set.PoleFeatureId, StringComparer.Ordinal)
            .Select(set => new PlannedCommit(
                set,
                targets[set.TargetId].AssetTypeId,
                OccupancyChangeApplier.Apply(live.GetValueOrDefault(set.PoleFeatureId), set).Entries))
            .ToList();

        return new CommitPlan(commits, toDiscard);
    }

    /// <summary>A set is committed unless its target was skipped or found not executable; then it is discarded (OCC-B11).</summary>
    private static (List<OccupancyChangeSet> Commit, List<OccupancyChangeSet> Discard) Partition(
        List<OccupancyChangeSet> staged, Dictionary<string, ServiceOrderTarget> targets)
    {
        var commit = new List<OccupancyChangeSet>(staged.Count);
        var discard = new List<OccupancyChangeSet>();
        foreach (var set in staged)
        {
            // A set exists only as long as its target, so the target is on file.
            var status = targets[set.TargetId].Status;
            if (status is OrderTargetStatus.Skipped or OrderTargetStatus.NotExecutable)
                discard.Add(set);
            else
                commit.Add(set);
        }

        return (commit, discard);
    }

    private async Task<List<ServiceOrderTarget>> AllTargetsAsync(string orderId, CancellationToken ct)
    {
        var all = new List<ServiceOrderTarget>();
        for (var page = 1; ; page++)
        {
            var result = await _targets
                .GetPageAsync(new OrderTargetQuery(orderId, page, OrderTargetQuery.MaxPageSize), ct)
                .ConfigureAwait(false);
            all.AddRange(result.Items);

            if (result.Items.Count < OrderTargetQuery.MaxPageSize || all.Count >= result.TotalCount)
                return all;
        }
    }

    private async Task<List<OccupancyChangeSet>> AllStagedAsync(string orderId, CancellationToken ct)
    {
        var all = new List<OccupancyChangeSet>();
        for (var page = 1; ; page++)
        {
            var result = await _changeSets
                .GetPageAsync(new OccupancyChangeSetQuery(orderId, page, OccupancyChangeSetQuery.MaxPageSize, OccupancyChangeSetStatus.Staged), ct)
                .ConfigureAwait(false);
            all.AddRange(result.Items);

            if (result.Items.Count < OccupancyChangeSetQuery.MaxPageSize || all.Count >= result.TotalCount)
                return all;
        }
    }

    // ── Write ─────────────────────────────────────────────────────────────────

    private async Task<List<(PlannedCommit Commit, PoleOccupancy Pole)>> WriteAsync(
        ServiceOrder order, OrderType orderType, string approvedStatus, CommitPlan plan,
        WorkflowPrincipal principal, DateTimeOffset now, CancellationToken ct)
    {
        var written = new List<(PlannedCommit, PoleOccupancy)>(plan.Commits.Count);
        foreach (var commit in plan.Commits)
        {
            var set = commit.Set;
            var pole = await _occupancyWriter
                .ReplaceEntriesAsync(set.PoleFeatureId, set.BaselineVersion, commit.Entries, ct)
                .ConfigureAwait(false);

            if (!await _changeSetWriter.MarkCommittedAsync(set.Commit(now), ct).ConfigureAwait(false))
                throw new OccupancyConflictException([set.PoleFeatureId]);

            written.Add((commit, pole));
        }

        foreach (var set in plan.Discards)
            await _changeSetWriter.DiscardAsync(set.OrderId, set.TargetId, ct).ConfigureAwait(false);

        // After the sets are committed: closing the order discards whatever is still staged on it.
        await _orders.UpdateAsync(Approved(order, orderType, approvedStatus, now), ct).ConfigureAwait(false);
        await _orders.AppendActionAsync(
            order.Id,
            new OrderActionLog(
                OrderAction.Approve, principal.UserId, now.UtcDateTime,
                $"Approved: {plan.Commits.Count} occupancy change set(s) committed.", approvedStatus)
            {
                ActorKind = principal.Kind,
            },
            ct).ConfigureAwait(false);

        if (written.Count > 0)
            await EnqueueAttributesAsync(order, principal, written, ct).ConfigureAwait(false);

        return written;
    }

    private Task<Guid> EnqueueAttributesAsync(
        ServiceOrder order, WorkflowPrincipal principal, List<(PlannedCommit Commit, PoleOccupancy Pole)> written, CancellationToken ct)
    {
        var mutations = written
            .Select(item => new AssetFeatureMutationItem(
                FeatureId: item.Pole.PoleFeatureId,
                MutationKind: AssetMutationKind.UpdateAttributes,
                Name: string.Empty,
                AssetTypeId: item.Commit.AssetTypeId,
                Description: string.Empty,
                LayerId: string.Empty,
                Geometry: null,
                TopologyEdges: [],
                CustomAttributes: OccupancyAssetAttributes.For(item.Pole)))
            .ToList();

        var batch = AssetMutationBatchRequestedEvent.Create(
            catalogInstanceId: _options.CatalogInstanceId,
            organizationId: order.OrganizationId,
            requestedByService: RequestedByService,
            performedByUserId: principal.UserId,
            mutations: mutations,
            timeProvider: _timeProvider,
            batchId: AssetMutationBatchId(order.Id),
            sourceOrderId: order.Id,
            sourceOrderTypeId: order.OrderTypeId,
            scopeId: (order as IScopeableResource)?.ScopeId,
            correlationId: order.Id);

        return _outbox.EnqueueAsync(
            batch,
            IntegrationEventTypes.AssetMutationBatchRequestedV1,
            partitionKey: order.Id,
            catalogInstanceId: _options.CatalogInstanceId,
            correlationId: order.Id,
            causationId: CausationId(order.Id),
            ct: ct);
    }

    /// <summary>The order as the approval stores it: moved to <paramref name="approvedStatus"/>, stamped, carrying the row version it was read with.</summary>
    private static ServiceOrder Approved(ServiceOrder order, OrderType orderType, string approvedStatus, DateTimeOffset now) => new()
    {
        Id = order.Id,
        RowVersion = order.RowVersion,
        Title = order.Title,
        Description = order.Description,
        OrderTypeId = order.OrderTypeId,
        Status = approvedStatus,
        Priority = order.Priority,
        CreatedBy = order.CreatedBy,
        AssignedTo = order.AssignedTo,
        OrganizationId = order.OrganizationId,
        CreatedAt = order.CreatedAt,
        UpdatedAt = now.UtcDateTime,
        ScheduledAt = order.ScheduledAt,
        CompletedAt = ServiceOrderTransitions.IsSuccessState(orderType, approvedStatus) ? now.UtcDateTime : order.CompletedAt,
        Attributes = new Dictionary<string, string>(order.Attributes, StringComparer.Ordinal),
        Features = [.. order.Features],
        SelectionSpec = order.SelectionSpec,
        FeatureIds = [.. order.FeatureIds],
        ParentOrderId = order.ParentOrderId,
        ChildOrderIds = [.. order.ChildOrderIds],
        Dispatches = [.. order.Dispatches],
        ActionLog = [.. order.ActionLog],
    };

    private sealed record PlannedCommit(OccupancyChangeSet Set, string AssetTypeId, IReadOnlyList<PoleOccupancyEntry> Entries);

    private sealed record CommitPlan(IReadOnlyList<PlannedCommit> Commits, IReadOnlyList<OccupancyChangeSet> Discards);
}
