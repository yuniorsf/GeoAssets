using System.Collections.ObjectModel;
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

    /// <summary>
    /// Role names the principal holds on organizations other than its own, keyed by that organization (XD01-326) — the
    /// Identity-independent copy of <c>AuthorizationContext.ScopedRoles</c> (XD01-325). Never part of
    /// <see cref="RoleNames"/>; read through <see cref="RoleNamesOn"/>. Defaults to empty, so every existing caller is
    /// unaffected.
    /// </summary>
    public IReadOnlyDictionary<Guid, IReadOnlyList<string>> ScopedRoleNames { get; init; }
        = ReadOnlyDictionary<Guid, IReadOnlyList<string>>.Empty;

    /// <summary>
    /// Permission codes granted by <see cref="ScopedRoleNames"/>, keyed by organization (XD01-326) — the copy of
    /// <c>AuthorizationContext.ScopedPermissions</c>. Never part of <see cref="PermissionCodes"/>; read through
    /// <see cref="PermissionCodesOn"/>. What a grant with <see cref="WorkflowOrgGrant.RequiresScopedRole"/> is decided by.
    /// </summary>
    public IReadOnlyDictionary<Guid, IReadOnlyList<string>> ScopedPermissionCodes { get; init; }
        = ReadOnlyDictionary<Guid, IReadOnlyList<string>>.Empty;

    /// <summary>
    /// Role names the principal holds on <paramref name="organizationId"/>: <see cref="RoleNames"/> for its own
    /// organization, otherwise only the roles assigned on that organization.
    /// </summary>
    public IReadOnlyList<string> RoleNamesOn(Guid organizationId) => Resolve(organizationId, RoleNames, ScopedRoleNames);

    /// <summary>
    /// Permission codes the principal holds on <paramref name="organizationId"/>: <see cref="PermissionCodes"/> for its
    /// own organization, otherwise only those of the roles assigned on that organization.
    /// </summary>
    public IReadOnlyList<string> PermissionCodesOn(Guid organizationId) => Resolve(organizationId, PermissionCodes, ScopedPermissionCodes);

    /// <summary>Whether the principal holds <paramref name="role"/> on <paramref name="organizationId"/> (case-insensitive).</summary>
    public bool HasRoleOn(Guid organizationId, string role)
        => RoleNamesOn(organizationId).Contains(role, StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether the principal holds <paramref name="code"/> on <paramref name="organizationId"/> (case-insensitive).</summary>
    public bool HasPermissionOn(Guid organizationId, string code)
        => PermissionCodesOn(organizationId).Contains(code, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// This principal as it stands on <paramref name="organizationId"/>: same user, organization, groups and grants, with
    /// <see cref="RoleNames"/> and <see cref="PermissionCodes"/> replaced by <see cref="RoleNamesOn"/> and
    /// <see cref="PermissionCodesOn"/> — what the rule chain sees for a principal that reaches an order only through a
    /// grant with <see cref="WorkflowOrgGrant.RequiresScopedRole"/> (XD01-326).
    /// </summary>
    internal WorkflowPrincipal OnOrganization(Guid organizationId) => this with
    {
        RoleNames = RoleNamesOn(organizationId),
        PermissionCodes = PermissionCodesOn(organizationId),
    };

    public bool HasRole(string role)
        => RoleNames.Contains(role, StringComparer.OrdinalIgnoreCase);

    public bool HasPermission(string code)
        => PermissionCodes.Contains(code, StringComparer.OrdinalIgnoreCase);

    public bool BelongsToGroup(string groupId)
        => GroupIds.Contains(groupId, StringComparer.OrdinalIgnoreCase);

    public bool BelongsToOrganization(string orgId)
        => OrganizationId != null &&
           OrganizationId.Equals(orgId, StringComparison.OrdinalIgnoreCase);

    private IReadOnlyList<string> Resolve(
        Guid organizationId,
        IReadOnlyList<string> home,
        IReadOnlyDictionary<Guid, IReadOnlyList<string>> scoped)
    {
        if (Guid.TryParse(OrganizationId, out var own) && own == organizationId)
            return home;

        return scoped.TryGetValue(organizationId, out var values) ? values : [];
    }
}
