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
);
