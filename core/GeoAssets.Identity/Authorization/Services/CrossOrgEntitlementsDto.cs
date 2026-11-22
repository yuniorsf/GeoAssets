using GeoAssets.Identity.Authorization.Models;

namespace GeoAssets.Identity.Authorization.Services;

/// <summary>
/// Wire shape for <c>GET /api/identity/me/cross-org-entitlements</c> (XD01-329): everything the caller is entitled to on
/// organizations other than its own, and nothing about anyone else.
/// <list type="bullet">
/// <item><see cref="Grants"/> — the active, unexpired <see cref="OrganizationGrant"/>s held by the caller's own organization,
/// each with the resource organization's name.</item>
/// <item><see cref="ScopedRoles"/> — the roles the caller holds on other organizations (XD01-325) and the permission codes
/// they grant there, per organization.</item>
/// </list>
/// Together they are what a host needs to build the <c>WorkflowPrincipal</c> the kernel builds
/// (<c>ServerWorkflowPrincipalFactory</c>: <c>OrgGrants</c>, <c>ScopedRoleNames</c>, <c>ScopedPermissionCodes</c>), so
/// <c>ServiceOrderRules</c> decides the same on both sides. <c>GET /api/identity/me</c> stays home-only: its roles and
/// permissions are the ones held in the caller's own organization, as <see cref="AuthorizationContext.Roles"/> and
/// <see cref="AuthorizationContext.Permissions"/> are. Shared by <c>GeoAssets.Server</c> (produces it with
/// <see cref="Create"/>) and the REST-backed clients (consume it), so the shape cannot drift between them.
/// </summary>
public sealed record CrossOrgEntitlementsDto(
    IReadOnlyList<CrossOrgGrantEntitlementDto> Grants,
    IReadOnlyList<ScopedRoleEntitlementDto>    ScopedRoles)
{
    /// <summary>No entitlement: what a caller with no cross-organization grant or scoped role gets.</summary>
    public static CrossOrgEntitlementsDto None { get; } = new([], []);

    /// <summary>
    /// The caller's entitlements: <paramref name="activeGrants"/> (the active, unexpired grants held by its organization)
    /// and the scoped roles and permissions of <paramref name="context"/>. <paramref name="organizationNames"/> names the
    /// organizations involved; one it does not name is sent without a name.
    /// </summary>
    public static CrossOrgEntitlementsDto Create(
        AuthorizationContext context,
        IReadOnlyList<OrganizationGrant> activeGrants,
        IReadOnlyDictionary<Guid, string> organizationNames)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(activeGrants);
        ArgumentNullException.ThrowIfNull(organizationNames);

        var grants = activeGrants
            .Select(g => CrossOrgGrantEntitlementDto.FromModel(g, organizationNames.GetValueOrDefault(g.ResourceOrganizationId)))
            .ToList();

        var scoped = context.ScopedRoles.Keys
            .Union(context.ScopedPermissions.Keys)
            .Order()
            .Select(organizationId => new ScopedRoleEntitlementDto(
                organizationId,
                organizationNames.GetValueOrDefault(organizationId),
                [.. context.ScopedRoles.GetValueOrDefault(organizationId) ?? []],
                [.. context.ScopedPermissions.GetValueOrDefault(organizationId) ?? []]))
            .ToList();

        return new CrossOrgEntitlementsDto(grants, scoped);
    }

    /// <summary>The organizations <see cref="Create"/> needs named: every grant's resource organization and every scoped one.</summary>
    public static IReadOnlyList<Guid> OrganizationsToName(AuthorizationContext context, IReadOnlyList<OrganizationGrant> activeGrants)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(activeGrants);

        return [.. activeGrants.Select(g => g.ResourceOrganizationId)
            .Union(context.ScopedRoles.Keys)
            .Union(context.ScopedPermissions.Keys)];
    }

    /// <summary>The name of <paramref name="organizationId"/> as an entitlement carries it; null when none names it.</summary>
    public string? OrganizationName(Guid organizationId) =>
        (Grants ?? []).Where(g => g.ResourceOrganizationId == organizationId).Select(g => g.ResourceOrganizationName)
            .Concat((ScopedRoles ?? []).Where(s => s.OrganizationId == organizationId).Select(s => s.OrganizationName))
            .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name));

    /// <summary>The grants as held by <paramref name="granteeOrganizationId"/>, the caller's organization.</summary>
    public IReadOnlyList<OrganizationGrant> ToGrants(Guid granteeOrganizationId) =>
        [.. (Grants ?? []).Select(g => g.ToModel(granteeOrganizationId))];

    /// <summary>The roles held on each other organization — <see cref="AuthorizationContext.ScopedRoles"/>.</summary>
    public IReadOnlyDictionary<Guid, IReadOnlyList<string>> ToScopedRoles() =>
        (ScopedRoles ?? []).ToDictionary(s => s.OrganizationId, s => (IReadOnlyList<string>)[.. s.Roles ?? []]);

    /// <summary>The permission codes held on each other organization — <see cref="AuthorizationContext.ScopedPermissions"/>.</summary>
    public IReadOnlyDictionary<Guid, IReadOnlyList<string>> ToScopedPermissions() =>
        (ScopedRoles ?? []).ToDictionary(s => s.OrganizationId, s => (IReadOnlyList<string>)[.. s.Permissions ?? []]);
}
