namespace GeoAssets.Workflow.Orders;

/// <summary>
/// Describes a category of service order (e.g. inspection, maintenance, emergency).
///
/// Each type carries:
///   • <see cref="CreationPolicies"/> — who is allowed to create orders of this type
///   • <see cref="ActionPermissions"/> — which roles/permissions are required per action
///
/// Register order types in the host via <see cref="OrderTypeRegistry"/>.
/// </summary>
public sealed class OrderType
{
    /// <summary>Machine-readable identifier, e.g. "inspection" or "emergency-repair".</summary>
    public required string Id          { get; init; }

    public required string DisplayName { get; init; }
    public string          Description { get; init; } = string.Empty;

    /// <summary>
    /// Policies that gate order creation. A user must satisfy AT LEAST ONE policy
    /// (any-match) unless the list is empty, in which case creation is unrestricted.
    /// </summary>
    public List<OrderCreationPolicy> CreationPolicies  { get; init; } = [];

    /// <summary>
    /// Per-action overrides for this order type.
    /// When absent for a given action, <see cref="ServiceOrderRules"/> falls back to
    /// the global defaults.
    /// </summary>
    public List<OrderActionPermission> ActionPermissions { get; init; } = [];

    /// <summary>
    /// Optional JSON Schema (draft 2020-12) text validating this order type's
    /// <see cref="ServiceOrder.Attributes"/>. Null or empty means unrestricted — any
    /// key/value pairs are accepted (same "empty = unrestricted" convention as
    /// <see cref="CreationPolicies"/>). Enforced by <see cref="ServiceOrderAttributeValidator"/>,
    /// applied on every write by <see cref="ValidatingServiceOrderRepository"/>.
    /// </summary>
    public string? AttributesSchemaJson { get; init; }

    /// <summary>
    /// The states this order type's workflow graph can be in. Empty means "use the
    /// global default graph" (<see cref="ServiceOrderTransitions"/>'s built-in
    /// Draft/Pending/InProgress/OnHold/Completed/Cancelled dictionary) — same
    /// "empty = unrestricted/default" convention as <see cref="CreationPolicies"/> and
    /// <see cref="AttributesSchemaJson"/>. When non-empty, <see cref="Transitions"/>
    /// defines the only legal moves between them (see
    /// <see cref="ServiceOrderTransitions.IsValid(OrderType?, string, string)"/>).
    /// </summary>
    public List<WorkflowState> States { get; init; } = [];

    /// <summary>
    /// The legal edges between <see cref="States"/>. Only consulted when
    /// <see cref="States"/> is non-empty. Carries no permission data — authorization
    /// stays entirely <see cref="ServiceOrderRules"/>'s job (extended with
    /// <see cref="OrderActionPermission.FromStateKey"/> for per-state overrides), not a
    /// second, weaker mechanism duplicated here.
    /// </summary>
    public List<WorkflowTransition> Transitions { get; init; } = [];

    /// <summary>
    /// The state a new order of this type starts in. Null means "use
    /// <see cref="ServiceOrderStatus.Draft"/>" (<see cref="ServiceOrder.Status"/>'s own
    /// default), so leaving this unset changes nothing for order types that don't
    /// define a custom graph.
    /// </summary>
    public string? InitialStateKey { get; init; }
}

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

// ── Supporting value types ─────────────────────────────────────────────────────

/// <summary>
/// A single policy gate for order creation.
/// The caller satisfies the policy if their identity matches <see cref="Value"/>
/// according to <see cref="Kind"/>.
/// </summary>
public sealed record OrderCreationPolicy(PolicyKind Kind, string Value);

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

/// <summary>The dimension along which a policy requirement is expressed.</summary>
public enum PolicyKind
{
    /// <summary>Match by role name (e.g. "Supervisor").</summary>
    Role,

    /// <summary>Match by permission code (e.g. "serviceorders:approve").</summary>
    Permission,

    /// <summary>Match by group ID.</summary>
    Group,

    /// <summary>Match by organization ID.</summary>
    Organization,
}
