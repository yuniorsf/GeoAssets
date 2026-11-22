namespace GeoAssets.Workflow.Rules;

/// <summary>
/// A cross-organization access grant, resolved from Identity's <c>OrganizationGrant</c> and
/// attached to a <see cref="WorkflowPrincipal"/> (see <see cref="WorkflowPrincipal.OrgGrants"/>)
/// so <see cref="CrossOrgGrantRule"/> can evaluate it without <c>GeoAssets.Workflow</c>
/// referencing <c>GeoAssets.Identity</c> — same reasoning as <see cref="WorkflowPrincipal"/>
/// itself (XD01-22).
///
/// <paramref name="ScopeId"/> (XD01-250) further restricts the grant to resources whose own
/// <c>IScopeableResource.ScopeId</c> matches — e.g. one município's worth of a Distribuidora's
/// orders, not everything it owns. Null means ungated within <see cref="ResourceType"/>, the
/// original behavior from before this field existed.
/// </summary>
public sealed record WorkflowOrgGrant(
    string                ResourceOrganizationId,
    string?               ResourceType,
    IReadOnlyList<string> AllowedActions,
    string?               RequiredRole,
    string?               ScopeId = null
)
{
    /// <summary>
    /// When the grant stops applying (UTC); null means it never expires. A host resolves only
    /// grants that are active and unexpired when it builds the principal — this lets the rule
    /// engine also refuse one that has expired since, which matters for a principal that lives
    /// as long as a client session (XD01-332).
    /// </summary>
    public DateTime? ExpiresAt { get; init; }

    /// <summary>
    /// How the principal qualifies for the actions the grant lists (XD01-326). False (the default, and every grant from
    /// before this field): as today — the grant opens the order to the principal's organization and its home roles decide,
    /// with <see cref="RequiredRole"/> matched against <see cref="WorkflowPrincipal.RoleNames"/>. True: only through the
    /// permissions the principal holds on <see cref="ResourceOrganizationId"/>
    /// (<see cref="WorkflowPrincipal.HasPermissionOn"/>); home roles and permissions count for nothing, and
    /// <see cref="RequiredRole"/> is matched against <see cref="WorkflowPrincipal.RoleNamesOn"/>.
    /// </summary>
    public bool RequiresScopedRole { get; init; }

    /// <summary>
    /// The order types the grant is narrowed to (XD01-326), compared with <see cref="Orders.IServiceOrder.OrderTypeId"/>
    /// ignoring case. Empty (the default) means every order type.
    /// </summary>
    public IReadOnlyList<string> OrderTypeIds { get; init; } = [];
}
