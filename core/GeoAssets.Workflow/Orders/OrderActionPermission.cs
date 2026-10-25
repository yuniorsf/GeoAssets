namespace GeoAssets.Workflow.Orders;

/// <summary>
/// Maps an action to a required role or permission code for a specific order type.
/// </summary>
/// <param name="FromStateKey">
/// When set, this permission only applies while the order is in this state — e.g.
/// "Approve requires role X, but only from the Pending state." Null (the default)
/// applies regardless of state, matching every permission's behavior before this
/// parameter existed.
/// </param>
public sealed record OrderActionPermission(OrderActionType Action, PolicyKind Kind, string Value, string? FromStateKey = null);
