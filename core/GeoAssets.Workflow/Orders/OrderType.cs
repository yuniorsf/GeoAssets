using GeoAssets.Workflow.Targets;

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

    /// <summary>
    /// Opts this order type into per-target work (which asset types its orders may target,
    /// the selection limit, per-target activities, overlap handling, fan-out). Null means
    /// "no target policy": orders of this type keep the flat
    /// <see cref="ServiceOrder.FeatureIds"/> behaviour unchanged. Validated by
    /// <see cref="OrderTypeRegistry"/> whenever the type enters the registry.
    /// </summary>
    public OrderTargetPolicy? TargetPolicy { get; init; }

    /// <summary>
    /// What the organization an order of this type was dispatched to may do with it (XD01-333).
    /// Defaults to <see cref="OrderRecipientAccess.Inform"/> — read-only on the order and its
    /// targets — so a type that does not say otherwise gives a recipient nothing to change.
    /// </summary>
    public OrderRecipientAccess RecipientAccess { get; init; } = OrderRecipientAccess.Inform;

    /// <summary>
    /// The order actions a recipient of an order of this type may take on top of what
    /// <see cref="RecipientAccess"/> gives it (XD01-336) — e.g. accept a notice and comment on it
    /// while the notice stays read-only. Still a ceiling, never a grant: the rule chain has to
    /// allow the acting user the action too. An action that moves the order's status moves it only
    /// along a transition of this type tagged with that action
    /// (<see cref="ServiceOrderTransitions.TransitionsFor"/>); one with no such transition leaves
    /// the status as it is. Empty (the default) adds nothing.
    /// </summary>
    public List<OrderActionType> RecipientActions { get; init; } = [];
}
