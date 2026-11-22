using GeoAssets.Workflow.Rules;

namespace GeoAssets.Workflow.Persistence;

/// <summary>
/// What a caller may reach in other organizations, as this host's mirrors know it (XD01-328): the active, unexpired grants
/// its organization holds (<see cref="WorkflowPrincipal.OrgGrants"/>) and the roles and permissions it holds on other
/// organizations (<see cref="WorkflowPrincipal.ScopedRoleNames"/>/<see cref="WorkflowPrincipal.ScopedPermissionCodes"/>) —
/// the pieces of a <see cref="WorkflowPrincipal"/> a host without the identity database builds from
/// <c>OrganizationGrantMirror</c> and <c>ScopedRoleAssignmentMirror</c>, so <see cref="ServiceOrderRules"/> decides
/// cross-organization access there exactly as it does in the kernel (XD01-326).
/// </summary>
public sealed record MirroredEntitlements(
    IReadOnlyList<WorkflowOrgGrant> Grants,
    IReadOnlyDictionary<Guid, IReadOnlyList<string>> ScopedRoleNames,
    IReadOnlyDictionary<Guid, IReadOnlyList<string>> ScopedPermissionCodes)
{
    /// <summary>Nothing in any other organization.</summary>
    public static readonly MirroredEntitlements None = new([], new Dictionary<Guid, IReadOnlyList<string>>(), new Dictionary<Guid, IReadOnlyList<string>>());
}
