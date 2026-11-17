using GeoAssets.Core.Isp.NeutralOperator.Occupancy;
using GeoAssets.Core.Messaging;
using GeoAssets.Core.Messaging.Events;
using GeoAssets.Core.Models;
using GeoAssets.Workflow.Notifications;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using GeoAssets.Workflow.Targets;

namespace GeoAssets.Workflow.Isp.NeutralOperator.SharedUse;

/// <summary>
/// Plans and executes the fan-out of a shared-use order (XD01-316) on ports only: what to do is
/// <see cref="SharedUseDispatchPlanner"/>'s decision, this class reads what the planner needs,
/// checks the caller, and writes the plan through <see cref="IWorkflowUnitOfWork"/>.
///
/// <para><b>Order of checks, every member:</b> arguments; unknown order
/// (<see cref="KeyNotFoundException"/>); <see cref="ServiceOrderRules"/> for
/// <see cref="OrderActionType.Dispatch"/> (a caller outside the organization gate is refused
/// there); <b>owner only</b> — a caller the rules allowed whose organization is not the order's
/// is refused too, because the occupancy the plan is made from belongs to the pole owner and its
/// store answers no other organization; the order's type and its fan-out; the dispatch
/// transition from the order's status. Only then are targets and occupancy read.</para>
///
/// <para><b>The write</b> starts with the parent — its dispatch transition, guarded by the
/// caller's row version — so two dispatches of one order queue on that row. Under it the
/// parent's children are read again, and only the planned children still missing are created:
/// each with its dispatch, its targets and its notification on the outbox, staged while that
/// child's span is current. One action-log entry on the parent closes the unit.</para>
/// </summary>
public sealed class SharedUseDispatchService : ISharedUseDispatchService, ISharedUseDispatchPlanReader
{
    internal const string OwnerOnlyReason =
        "A shared-use order is dispatched only by members of the organization that owns its poles.";

    /// <summary>Metadata key of a child's notification holding the parent order's id.</summary>
    public const string ParentOrderMetadataKey = "parentOrderId";

    /// <summary>Metadata key of a child's notification holding the occupant organization's id.</summary>
    public const string PartyOrganizationMetadataKey = "partyOrganizationId";

    private readonly IServiceOrderRepository _orders;
    private readonly OrderTypeRegistry _orderTypes;
    private readonly ServiceOrderRules _rules;
    private readonly IServiceOrderTargetReader _targets;
    private readonly IServiceOrderTargetWriter _targetWriter;
    private readonly IPoleOccupancyReader _occupancy;
    private readonly IOutboxWriter _outbox;
    private readonly IWorkflowUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;
    private readonly ISharedUseFanOutTracer? _tracer;

    public SharedUseDispatchService(
        IServiceOrderRepository orders,
        OrderTypeRegistry orderTypes,
        ServiceOrderRules rules,
        IServiceOrderTargetReader targets,
        IServiceOrderTargetWriter targetWriter,
        IPoleOccupancyReader occupancy,
        IOutboxWriter outbox,
        IWorkflowUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        ISharedUseFanOutTracer? tracer = null)
    {
        ArgumentNullException.ThrowIfNull(orders);
        ArgumentNullException.ThrowIfNull(orderTypes);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(targetWriter);
        ArgumentNullException.ThrowIfNull(occupancy);
        ArgumentNullException.ThrowIfNull(outbox);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _orders = orders;
        _orderTypes = orderTypes;
        _rules = rules;
        _targets = targets;
        _targetWriter = targetWriter;
        _occupancy = occupancy;
        _outbox = outbox;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
        _tracer = tracer;
    }

    /// <summary>The causation id every notification of one parent's fan-out carries.</summary>
    public static string CausationId(string parentOrderId) => $"shared-use-dispatch:{parentOrderId}";

    // ── Reads ─────────────────────────────────────────────────────────────────

    public async Task<SharedUseDispatchPreview> PreviewAsync(
        string orderId, int page, int pageSize, WorkflowPrincipal principal, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
        ArgumentNullException.ThrowIfNull(principal);
        SharedUsePaging.Check(page, pageSize);
        var (order, _, childType, plan) = await PlanAsync(orderId, principal, ct).ConfigureAwait(false);
        var newRecipients = plan.NewChildren.Count();

        return new SharedUseDispatchPreview
        {
            OrderId = order.Id,
            OrderTypeId = order.OrderTypeId,
            ChildOrderTypeId = childType.Id,
            RowVersion = order.RowVersion,
            StatusAfterDispatch = plan.ParentStatusAfterDispatch,
            TargetCount = plan.TargetCount,
            BlockedTargetCount = plan.BlockedTargetCount,
            TargetsWithoutActivityCount = plan.TargetIdsWithoutActivity.Count,
            UnnotifiablePoleCount = plan.UnnotifiablePoleIds.Count,
            SkippedEntryCount = plan.SkippedEntries.Count,
            NewRecipientCount = newRecipients,
            BlockReason = plan.BlockReason,
            CanDispatch = plan.BlockReason is null && newRecipients > 0,
            Recipients = SharedUsePaging.Slice(plan.Children, page, pageSize, child => new SharedUseDispatchRecipient(
                child.OccupantOrganizationId, child.ChildOrderId, child.Targets.Count, child.AlreadyDispatched)),
        };
    }

    public async Task<PagedResult<SharedUseSkippedEntry>> GetSkippedEntriesAsync(
        string orderId, int page, int pageSize, WorkflowPrincipal principal, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
        ArgumentNullException.ThrowIfNull(principal);
        SharedUsePaging.Check(page, pageSize);
        var (_, _, _, plan) = await PlanAsync(orderId, principal, ct).ConfigureAwait(false);

        return SharedUsePaging.Slice(plan.SkippedEntries, page, pageSize, entry => entry);
    }

    public async Task<PagedResult<string>> GetUnnotifiablePolesAsync(
        string orderId, int page, int pageSize, WorkflowPrincipal principal, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
        ArgumentNullException.ThrowIfNull(principal);
        SharedUsePaging.Check(page, pageSize);
        var (_, _, _, plan) = await PlanAsync(orderId, principal, ct).ConfigureAwait(false);

        return SharedUsePaging.Slice(plan.UnnotifiablePoleIds, page, pageSize, poleId => poleId);
    }

    // ── Write ─────────────────────────────────────────────────────────────────

    public async Task<SharedUseDispatchResult> DispatchAsync(
        string orderId, byte[] expectedRowVersion, WorkflowPrincipal principal, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
        ArgumentNullException.ThrowIfNull(expectedRowVersion);
        ArgumentNullException.ThrowIfNull(principal);
        var (order, _, _, plan) = await PlanAsync(orderId, principal, ct).ConfigureAwait(false);
        plan.EnsureDispatchable();

        var missing = plan.NewChildren.ToList();
        if (missing.Count == 0)
            return Result(order, plan, order.Status, created: 0);

        if (order is not ServiceOrder parent)
            throw new InvalidOperationException($"Expected a {nameof(ServiceOrder)} instance, got {order.GetType().Name}.");

        // Last exit before the first write: a caller that gave up leaves the order undispatched.
        ct.ThrowIfCancellationRequested();

        var created = await _unitOfWork
            .ExecuteAsync(token => WriteAsync(parent, plan, missing, expectedRowVersion, principal, token), ct)
            .ConfigureAwait(false);

        return Result(order, plan, plan.ParentStatusAfterDispatch, created);
    }

    private async Task<int> WriteAsync(
        ServiceOrder parent, SharedUseDispatchPlan plan, List<SharedUsePlannedChild> missing,
        byte[] expectedRowVersion, WorkflowPrincipal principal, CancellationToken ct)
    {
        await _orders.UpdateAsync(Dispatched(parent, plan, expectedRowVersion), ct).ConfigureAwait(false);

        // Read again now that this unit holds the parent: a dispatch that finished in between
        // has already created some of these children, and they are left to it.
        var present = await ChildIdsAsync(parent.Id, ct).ConfigureAwait(false);

        var created = 0;
        foreach (var child in missing.Where(child => !present.Contains(child.ChildOrderId)))
        {
            using (_tracer?.BeginChild(parent.Id, child.ChildOrderId, child.OccupantOrganizationId))
            {
                await _orders.AddAsync(child.Order, ct).ConfigureAwait(false);
                await _targetWriter.AddRangeAsync(child.ChildOrderId, child.Targets, ct).ConfigureAwait(false);
                await EnqueueNotificationAsync(parent, child, plan.PlannedAt, principal, ct).ConfigureAwait(false);
            }

            created++;
        }

        if (created > 0)
        {
            await _orders.AppendActionAsync(
                parent.Id,
                new OrderActionLog(
                    OrderActionType.Dispatch, principal.UserId, plan.PlannedAt.UtcDateTime,
                    $"Shared-use dispatch: {created} occupant order(s) created.")
                {
                    ActorKind = principal.Kind,
                },
                ct).ConfigureAwait(false);
        }

        return created;
    }

    private Task<Guid> EnqueueNotificationAsync(
        ServiceOrder parent, SharedUsePlannedChild child, DateTimeOffset occurredAt, WorkflowPrincipal principal, CancellationToken ct)
    {
        // A child order is born dispatched, so it has no status before this one.
        var notification = new OrderStateChangedEvent(
            OrderId: child.ChildOrderId,
            OrderTypeId: child.Order.OrderTypeId,
            PreviousStatus: string.Empty,
            NewStatus: child.Order.Status,
            PerformedBy: principal.UserId,
            OccurredAt: occurredAt,
            CorrelationId: parent.Id,
            Metadata: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [ParentOrderMetadataKey] = parent.Id,
                [PartyOrganizationMetadataKey] = child.OccupantOrganizationId.ToString("D"),
            });

        return _outbox.EnqueueAsync(
            notification,
            IntegrationEventTypes.OrderStateChangedV1,
            partitionKey: child.ChildOrderId,
            correlationId: parent.Id,
            causationId: CausationId(parent.Id),
            ct: ct);
    }

    // ── Shared by the reads and the write ─────────────────────────────────────

    private async Task<(IServiceOrder Order, OrderType ParentType, OrderType ChildType, SharedUseDispatchPlan Plan)> PlanAsync(
        string orderId, WorkflowPrincipal principal, CancellationToken ct)
    {
        var order = await AuthorizeAsync(orderId, principal, ct).ConfigureAwait(false);
        var (parentType, childType) = FanOutTypes(order);
        SharedUseDispatchPlanner.StatusAfterDispatch(parentType, order.Status);

        var targets = await AllTargetsAsync(order.Id, ct).ConfigureAwait(false);
        var featureIds = targets.Select(t => t.FeatureId).Distinct(StringComparer.Ordinal).ToList();
        var usages = featureIds.Count == 0
            ? []
            : await _targets.FindOpenUsagesAsync(featureIds, order.Id, ct).ConfigureAwait(false);
        var levels = TargetConflictEvaluator.Evaluate(order, parentType.TargetPolicy!.Exclusivity, featureIds, usages);

        var openPoleIds = featureIds.Where(id => levels[id] != OrderTargetConflictLevel.Blocked).ToList();
        var occupancies = openPoleIds.Count == 0
            ? []
            : await _occupancy.GetForPolesAsync(openPoleIds, ct).ConfigureAwait(false);

        var plan = SharedUseDispatchPlanner.Plan(
            new SharedUseDispatchPlanRequest
            {
                Parent = order,
                ParentType = parentType,
                ChildType = childType,
                Targets = targets,
                ConflictLevels = levels,
                Occupancies = occupancies,
                ExistingChildOrderIds = await ChildIdsAsync(order.Id, ct).ConfigureAwait(false),
                DispatchedBy = principal.UserId,
                ActorKind = principal.Kind,
            },
            _timeProvider);

        return (order, parentType, childType, plan);
    }

    private async Task<IServiceOrder> AuthorizeAsync(string orderId, WorkflowPrincipal principal, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var order = await _orders.GetByIdAsync(orderId, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Service order '{orderId}' not found.");

        var verdict = _rules.Evaluate(principal, OrderActionType.Dispatch, order);
        if (!verdict.Allowed)
            throw new OrderActionDeniedException(orderId, verdict);

        if (!Guid.TryParse(principal.OrganizationId, out var organizationId) || organizationId != order.OrganizationId)
            throw new OrderActionDeniedException(orderId, RuleEvaluationResult.Deny(OrderActionType.Dispatch, OwnerOnlyReason));

        return order;
    }

    private (OrderType ParentType, OrderType ChildType) FanOutTypes(IServiceOrder order)
    {
        var parentType = _orderTypes.Find(order.OrderTypeId)
            ?? throw new OrderTypeNotRegisteredException(
                order.OrderTypeId, $"Order type '{order.OrderTypeId}' is not registered.");

        var fanOut = parentType.TargetPolicy?.FanOut
            ?? throw new OrderTypeHasNoFanOutException(
                parentType.Id, $"Order type '{parentType.Id}' declares no fan-out, so its orders are not dispatched to occupants.");

        var childType = _orderTypes.Find(fanOut.ChildOrderTypeId)
            ?? throw new OrderTypeNotRegisteredException(
                fanOut.ChildOrderTypeId, $"Order type '{fanOut.ChildOrderTypeId}', the fan-out child of '{parentType.Id}', is not registered.");

        return (parentType, childType);
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

    private async Task<HashSet<string>> ChildIdsAsync(string parentOrderId, CancellationToken ct)
    {
        var children = await _orders.GetChildrenAsync(parentOrderId, ct).ConfigureAwait(false);
        return children.Select(child => child.Id).ToHashSet(StringComparer.Ordinal);
    }

    private static SharedUseDispatchResult Result(IServiceOrder order, SharedUseDispatchPlan plan, string status, int created) => new()
    {
        ParentOrderId = order.Id,
        OrderTypeId = order.OrderTypeId,
        ParentStatus = status,
        ChildOrdersCreated = created,
        ChildOrdersAlreadyDispatched = plan.Children.Count - created,
        NotificationsEnqueued = created,
        TargetCount = plan.TargetCount,
        BlockedTargetCount = plan.BlockedTargetCount,
        UnnotifiablePoleCount = plan.UnnotifiablePoleIds.Count,
        SkippedEntryCount = plan.SkippedEntries.Count,
    };

    /// <summary>
    /// The parent as it is stored by the dispatch: moved along its dispatch transition, stamped,
    /// and carrying the caller's row version as the precondition of the write.
    /// </summary>
    private static ServiceOrder Dispatched(ServiceOrder parent, SharedUseDispatchPlan plan, byte[] expectedRowVersion) => new()
    {
        Id = parent.Id,
        RowVersion = expectedRowVersion,
        Title = parent.Title,
        Description = parent.Description,
        OrderTypeId = parent.OrderTypeId,
        Status = plan.ParentStatusAfterDispatch,
        Priority = parent.Priority,
        CreatedBy = parent.CreatedBy,
        AssignedTo = parent.AssignedTo,
        OrganizationId = parent.OrganizationId,
        CreatedAt = parent.CreatedAt,
        UpdatedAt = plan.PlannedAt.UtcDateTime,
        ScheduledAt = parent.ScheduledAt,
        CompletedAt = parent.CompletedAt,
        Attributes = parent.Attributes,
        Features = parent.Features,
        SelectionSpec = parent.SelectionSpec,
        FeatureIds = parent.FeatureIds,
        ParentOrderId = parent.ParentOrderId,
        ChildOrderIds = parent.ChildOrderIds,
        Dispatches = parent.Dispatches,
        ActionLog = parent.ActionLog,
    };
}
