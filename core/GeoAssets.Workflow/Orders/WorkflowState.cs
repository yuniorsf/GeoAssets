namespace GeoAssets.Workflow.Orders;

/// <summary>
/// One node in an <see cref="OrderType"/>'s workflow graph.
/// </summary>
/// <param name="Key">Stable state key, stored as <see cref="ServiceOrder.Status"/>.</param>
/// <param name="DisplayName">Human-readable label for UI presentation.</param>
/// <param name="IsSuccess">
/// Whether reaching this state means the order finished successfully — drives
/// <see cref="ServiceOrder.CompletedAt"/> stamping the same way the literal
/// <see cref="ServiceOrderStatus.Completed"/> comparison does for order types with
/// no custom graph.
/// </param>
public sealed record WorkflowState(string Key, string DisplayName, bool IsSuccess = false);
