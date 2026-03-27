namespace GeoAssets.Workflow.Orders;

/// <summary>
/// Records a single dispatch event on a service order.
///
/// An order may be dispatched multiple times (e.g. first to an org, then re-dispatched
/// to a specific group after triage). Each dispatch is kept as an immutable record.
/// </summary>
public sealed record OrderDispatch(
    /// <summary>ID of the user, group, or organization that received this dispatch.</summary>
    string             TargetId,
    DispatchTargetType TargetType,

    /// <summary>User ID of whoever triggered the dispatch.</summary>
    string             DispatchedBy,
    DateTime           DispatchedAt,

    /// <summary>Optional note explaining the dispatch decision.</summary>
    string?            Note = null
);
