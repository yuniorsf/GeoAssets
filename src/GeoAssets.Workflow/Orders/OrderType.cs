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
}

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
public sealed record OrderActionPermission(OrderActionType Action, PolicyKind Kind, string Value);

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
