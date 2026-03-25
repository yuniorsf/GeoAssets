using GeoAssets.Core.Models;
using GeoAssets.Workflow.Selection;

namespace GeoAssets.Workflow.Orders;

/// <summary>
/// Represents a unit of field or analytical work over a set of geographic assets.
///
/// A service order:
///   • owns a collection of <see cref="GeoFeature"/> (the assets involved in the work)
///   • carries standard workflow metadata (status, priority, timestamps, assignee)
///   • participates in a tree hierarchy through parent / child relationships
///   • records the <see cref="FeatureSelectionSpec"/> that was used to populate its feature set,
///     enabling reproducibility and audit
/// </summary>
public interface IServiceOrder
{
    // ── Identity ─────────────────────────────────────────────────────────────

    string Id          { get; }
    string Title       { get; }
    string Description { get; }

    /// <summary>
    /// Identifies the <see cref="OrderType"/> this order belongs to
    /// (e.g. "inspection", "maintenance").
    /// </summary>
    string OrderTypeId { get; }

    // ── Workflow metadata ─────────────────────────────────────────────────────

    ServiceOrderStatus   Status   { get; }
    ServiceOrderPriority Priority { get; }

    string  CreatedBy   { get; }
    string? AssignedTo  { get; }

    DateTime  CreatedAt    { get; }
    DateTime? UpdatedAt    { get; }
    DateTime? ScheduledAt  { get; }
    DateTime? CompletedAt  { get; }

    /// <summary>Free-form key/value pairs for domain-specific metadata (e.g. work type, cost centre).</summary>
    IReadOnlyDictionary<string, string> Attributes { get; }

    // ── Geographic asset set ──────────────────────────────────────────────────

    /// <summary>The GeoFeatures involved in this order.</summary>
    IReadOnlyList<GeoFeature> Features { get; }

    /// <summary>
    /// Records how the feature set was originally built.
    /// Null when features were set programmatically without a named strategy.
    /// </summary>
    FeatureSelectionSpec? SelectionSpec { get; }

    // ── Hierarchy ─────────────────────────────────────────────────────────────

    /// <summary>ID of the parent order, or null if this is a root order.</summary>
    string? ParentOrderId { get; }

    /// <summary>IDs of direct child orders.</summary>
    IReadOnlyList<string> ChildOrderIds { get; }

    bool IsRoot  => ParentOrderId is null;
    bool IsLeaf  => ChildOrderIds.Count == 0;

    // ── Dispatch & audit ──────────────────────────────────────────────────────

    /// <summary>
    /// Ordered history of dispatch events.
    /// The last entry reflects the current dispatch target.
    /// </summary>
    IReadOnlyList<OrderDispatch>   Dispatches { get; }

    /// <summary>Chronological log of all actions performed on this order.</summary>
    IReadOnlyList<OrderActionLog>  ActionLog  { get; }
}
