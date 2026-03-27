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
