namespace GeoAssets.Workflow.Orders;

/// <summary>
/// Immutable audit record of an action performed on a service order.
/// </summary>
public sealed record OrderActionLog(
    OrderActionType Action,
    string          PerformedBy,
    DateTime        PerformedAt,
    string?         Comment    = null,
    ServiceOrderStatus? ResultingStatus = null
);
