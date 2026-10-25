namespace GeoAssets.Workflow.Orders;

/// <summary>
/// One legal edge between two <see cref="WorkflowState"/>s in an
/// <see cref="OrderType"/>'s workflow graph.
/// </summary>
/// <param name="FromStateKey">Source state key.</param>
/// <param name="ToStateKey">Destination state key.</param>
/// <param name="TriggerAction">
/// The <see cref="OrderActionType"/> that normally drives this edge, when there is
/// one unambiguous action for it (e.g. <see cref="OrderActionType.Cancel"/> for a
/// move to a cancelled state). Null when no single action maps to this edge (e.g.
/// suspend/resume edges with no dedicated action today) — the edge is still legal,
/// it's just not derivable from an action alone.
/// </param>
public sealed record WorkflowTransition(string FromStateKey, string ToStateKey, OrderActionType? TriggerAction = null);
