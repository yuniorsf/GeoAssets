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

    public ServiceOrderStatus   Status   { get; set; } = ServiceOrderStatus.Draft;
    public ServiceOrderPriority Priority { get; set; } = ServiceOrderPriority.Normal;

    public string  CreatedBy  { get; init; } = string.Empty;
    public string? AssignedTo { get; set;  }

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
    public List<string> ChildOrderIds { get; init; } = [];
    IReadOnlyList<string> IServiceOrder.ChildOrderIds => ChildOrderIds;

    // ── Dispatch & audit ──────────────────────────────────────────────────────

    public List<OrderDispatch>  Dispatches { get; init; } = [];
    public List<OrderActionLog> ActionLog  { get; init; } = [];

    IReadOnlyList<OrderDispatch>  IServiceOrder.Dispatches => Dispatches;
    IReadOnlyList<OrderActionLog> IServiceOrder.ActionLog  => ActionLog;

    // ── Status transitions ────────────────────────────────────────────────────

    /// <summary>Moves the order to <paramref name="newStatus"/> and stamps <see cref="UpdatedAt"/>.</summary>
    public ServiceOrder Transition(ServiceOrderStatus newStatus)
    {
        Status    = newStatus;
        UpdatedAt = DateTime.UtcNow;

        if (newStatus == ServiceOrderStatus.Completed)
            CompletedAt = DateTime.UtcNow;

        return this;
    }

    /// <summary>Appends a dispatch event and logs the action.</summary>
    public ServiceOrder DispatchTo(
        string             targetId,
        DispatchTargetType targetType,
        string             dispatchedBy,
        string?            note = null)
    {
        var dispatch = new OrderDispatch(targetId, targetType, dispatchedBy, DateTime.UtcNow, note);
        Dispatches.Add(dispatch);
        ActionLog.Add(new OrderActionLog(
            Action         : OrderActionType.Dispatch,
            PerformedBy    : dispatchedBy,
            PerformedAt    : dispatch.DispatchedAt,
            Comment        : note));
        UpdatedAt = DateTime.UtcNow;
        return this;
    }

    /// <summary>Records a performed action in the audit log and optionally transitions status.</summary>
    public ServiceOrder RecordAction(
        OrderActionType     action,
        string              performedBy,
        string?             comment        = null,
        ServiceOrderStatus? resultingStatus = null)
    {
        ActionLog.Add(new OrderActionLog(action, performedBy, DateTime.UtcNow, comment, resultingStatus));
        if (resultingStatus.HasValue)
            Transition(resultingStatus.Value);
        else
            UpdatedAt = DateTime.UtcNow;
        return this;
    }

    /// <summary>Replaces the feature set and records the spec used to build it.</summary>
    public ServiceOrder WithFeatures(IEnumerable<GeoFeature> features, FeatureSelectionSpec? spec = null)
    {
        Features.Clear();
        Features.AddRange(features);
        SelectionSpec = spec;
        UpdatedAt = DateTime.UtcNow;
        return this;
    }
}
