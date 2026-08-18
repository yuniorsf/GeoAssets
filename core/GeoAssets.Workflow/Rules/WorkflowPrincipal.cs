using GeoAssets.Workflow.Orders;

namespace GeoAssets.Workflow.Rules;

/// <summary>
/// A snapshot of a user's identity context for rule evaluation.
///
/// Built by the host layer from Identity data and passed into
/// <see cref="ServiceOrderRules"/>. Keeps the Workflow project
/// independent of <c>GeoAssets.Identity</c>.
///
/// Example (from a Blazor WASM host):
/// <code>
///   var principal = new WorkflowPrincipal(
///       UserId         : user.Id.ToString(),
///       OrganizationId : user.OrganizationId?.ToString(),
///       RoleNames      : roles.Select(r => r.Name).ToList(),
///       GroupIds       : groups.Select(g => g.Id.ToString()).ToList(),
///       PermissionCodes: permissions.Select(p => p.Code).ToList());
/// </code>
/// </summary>
public sealed record WorkflowPrincipal(
    string                    UserId,
    string?                   OrganizationId,
    IReadOnlyList<string>     RoleNames,
    IReadOnlyList<string>     GroupIds,
    IReadOnlyList<string>     PermissionCodes
)
{
    public static readonly WorkflowPrincipal Anonymous = new(
        UserId          : string.Empty,
        OrganizationId  : null,
        RoleNames       : [],
        GroupIds        : [],
        PermissionCodes : []);

    /// <summary>
    /// What kind of actor this principal represents. Defaults to <see cref="ActorKind.Human"/>
    /// so every existing caller is unaffected; an agent-issued principal sets this to
    /// <see cref="ActorKind.Agent"/>. Rule evaluation never branches on this value.
    /// </summary>
    public ActorKind Kind { get; init; } = ActorKind.Human;

    /// <summary>
    /// Active cross-organization grants letting this principal's organization reach other
    /// organizations' resources (XD01-22) — pre-resolved once per principal, same reasoning
    /// as <see cref="RoleNames"/>/<see cref="PermissionCodes"/>, since the specific resource
    /// being evaluated against isn't known yet when the principal is built. Defaults to empty
    /// so every existing caller is unaffected; see <see cref="CrossOrgGrantRule"/> for how
    /// it's consulted.
    /// </summary>
    public IReadOnlyList<WorkflowOrgGrant> OrgGrants { get; init; } = [];

    public bool HasRole(string role)
        => RoleNames.Contains(role, StringComparer.OrdinalIgnoreCase);

    public bool HasPermission(string code)
        => PermissionCodes.Contains(code, StringComparer.OrdinalIgnoreCase);

    public bool BelongsToGroup(string groupId)
        => GroupIds.Contains(groupId, StringComparer.OrdinalIgnoreCase);

    public bool BelongsToOrganization(string orgId)
        => OrganizationId != null &&
           OrganizationId.Equals(orgId, StringComparison.OrdinalIgnoreCase);
}
