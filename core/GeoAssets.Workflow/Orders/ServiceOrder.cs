using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Workflow.Selection;

namespace GeoAssets.Workflow.Orders;

/// <summary>
/// Default mutable implementation of <see cref="IServiceOrder"/>.
/// All mutation goes through the With* fluent methods to keep change tracking simple.
/// </summary>
public sealed class ServiceOrder : IServiceOrder
{
    // ── Identity ──────────────────────────────────────────────────────────────

    public string Id          { get; init; } = Guid.NewGuid().ToString();
    public string Title       { get; set;  } = string.Empty;
    public string Description { get; set;  } = string.Empty;
    public string OrderTypeId { get; set;  } = string.Empty;

    // ── Workflow metadata ─────────────────────────────────────────────────────

    public string               Status   { get; set; } = ServiceOrderStatus.Draft;
    public ServiceOrderPriority Priority { get; set; } = ServiceOrderPriority.Normal;

    public string  CreatedBy  { get; init; } = string.Empty;
    public string? AssignedTo { get; set;  }

    /// <summary>See <see cref="IOrgOwnedResource"/>. Set-once at creation, like <see cref="CreatedBy"/>.</summary>
    public Guid OrganizationId { get; init; } = Guid.Empty;

    public DateTime  CreatedAt   { get; init; } = DateTime.UtcNow;
    public DateTime? UpdatedAt   { get; set;  }
    public DateTime? ScheduledAt { get; set;  }
    public DateTime? CompletedAt { get; set;  }

    public Dictionary<string, string> Attributes { get; init; } = [];
    IReadOnlyDictionary<string, string> IServiceOrder.Attributes => Attributes;

    // ── Geographic asset set ──────────────────────────────────────────────────

    public List<GeoFeature> Features { get; init; } = [];
    IReadOnlyList<GeoFeature> IServiceOrder.Features => Features;

    public FeatureSelectionSpec? SelectionSpec { get; set; }

    /// <summary>
    /// Stable IDs of the features attached to this order.
    /// Populated when loading from a repository that does not hydrate full
    /// <see cref="GeoFeature"/> objects (e.g. the EF repository without an
    /// <c>IAssetProvider</c>).  Otherwise derived from <see cref="Features"/>.
    /// </summary>
    public string[] FeatureIds { get; set; } = [];

    // ── Hierarchy ─────────────────────────────────────────────────────────────

    public string?      ParentOrderId { get; set; }

    /// <summary>
    /// Derived from <see cref="ParentOrderId"/> by the repository on every read.
    /// Set <see cref="ParentOrderId"/> on the child to establish hierarchy —
    /// do not mutate this list directly, it will be overwritten on next load.
    /// </summary>
    public List<string> ChildOrderIds { get; init; } = [];
    IReadOnlyList<string> IServiceOrder.ChildOrderIds => ChildOrderIds;

    // ── Dispatch & audit ──────────────────────────────────────────────────────

    public List<OrderDispatch>  Dispatches { get; init; } = [];
    public List<OrderActionLog> ActionLog  { get; init; } = [];

    IReadOnlyList<OrderDispatch>  IServiceOrder.Dispatches => Dispatches;
    IReadOnlyList<OrderActionLog> IServiceOrder.ActionLog  => ActionLog;

    // ── Status transitions ────────────────────────────────────────────────────

    /// <summary>
    /// Moves the order to <paramref name="newStatus"/> and stamps <see cref="UpdatedAt"/>.
    /// Throws <see cref="InvalidServiceOrderTransitionException"/> if the transition is
    /// not structurally legal per <see cref="ServiceOrderTransitions.IsValid"/>.
    /// </summary>
    public ServiceOrder Transition(string newStatus, TimeProvider timeProvider)
    {
        if (!ServiceOrderTransitions.IsValid(Status, newStatus))
            throw new InvalidServiceOrderTransitionException(Status, newStatus);

        var now = timeProvider.GetUtcNow().UtcDateTime;

        Status    = newStatus;
        UpdatedAt = now;

        if (ServiceOrderTransitions.IsSuccessState(null, newStatus))
            CompletedAt = now;

        return this;
    }

    /// <summary>Appends a dispatch event and logs the action.</summary>
    public ServiceOrder DispatchTo(
        string             targetId,
        DispatchTargetType targetType,
        string             dispatchedBy,
        TimeProvider       timeProvider,
        string?            note              = null,
        ActorKind          actorKind         = ActorKind.Human,
        string?            agentInvocationId = null)
    {
        var now      = timeProvider.GetUtcNow().UtcDateTime;
        var dispatch = new OrderDispatch(targetId, targetType, dispatchedBy, now, note)
        {
            ActorKind         = actorKind,
            AgentInvocationId = agentInvocationId
        };
        Dispatches.Add(dispatch);
        ActionLog.Add(new OrderActionLog(
            Action         : OrderActionType.Dispatch,
            PerformedBy    : dispatchedBy,
            PerformedAt    : dispatch.DispatchedAt,
            Comment        : note)
        {
            ActorKind         = actorKind,
            AgentInvocationId = agentInvocationId
        });
        UpdatedAt = now;
        return this;
    }

    /// <summary>Records a performed action in the audit log and optionally transitions status.</summary>
    public ServiceOrder RecordAction(
        OrderActionType action,
        string          performedBy,
        TimeProvider    timeProvider,
        string?         comment           = null,
        string?         resultingStatus   = null,
        ActorKind       actorKind         = ActorKind.Human,
        string?         agentInvocationId = null)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        ActionLog.Add(new OrderActionLog(action, performedBy, now, comment, resultingStatus)
        {
            ActorKind         = actorKind,
            AgentInvocationId = agentInvocationId
        });
        if (resultingStatus is not null)
            Transition(resultingStatus, timeProvider);
        else
            UpdatedAt = now;
        return this;
    }

    /// <summary>Replaces the feature set and records the spec used to build it.</summary>
    public ServiceOrder WithFeatures(IEnumerable<GeoFeature> features, TimeProvider timeProvider, FeatureSelectionSpec? spec = null)
    {
        Features.Clear();
        Features.AddRange(features);
        SelectionSpec = spec;
        UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;
        return this;
    }
}
