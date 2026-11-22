using System.Collections.ObjectModel;
using GeoAssets.Identity.Authorization.Models;

namespace GeoAssets.Identity.Authorization.Services;

/// <summary>
/// Resolved authorization state for the current user.
/// Loaded once per request/evaluation and passed to policy evaluations
/// to avoid multiple round-trips to the database.
/// </summary>
public sealed class AuthorizationContext
{
    public required AppUser                   User        { get; init; }
    public required IReadOnlyList<string>     Roles       { get; init; }
    public required IReadOnlyList<UserClaim>  Claims      { get; init; }
    public required IReadOnlyList<string>     Permissions { get; init; }

    /// <summary>
    /// The active, unexpired cross-organization grants held by the user's own organization
    /// (XD01-332). Filled by the REST-backed clients from
    /// <c>GET /api/identity/organization-grants/me</c> (the Web client: from
    /// <c>GET /api/identity/me/cross-org-entitlements</c>, XD01-329) so that client-side service-order rule
    /// evaluation agrees with the server; empty wherever nothing resolves it — a server host
    /// reads grants from <c>IOrganizationGrantRepository</c> instead.
    /// </summary>
    public IReadOnlyList<OrganizationGrant>   OrganizationGrants { get; init; } = [];

    /// <summary>
    /// Role names the user holds on organizations other than their own, keyed by that
    /// organization (XD01-325). Never part of <see cref="Roles"/>; read through <see cref="RolesOn"/>.
    /// The Web client fills it from <c>GET /api/identity/me/cross-org-entitlements</c> (XD01-329);
    /// <c>GET /api/identity/me</c> carries home roles only.
    /// </summary>
    public IReadOnlyDictionary<Guid, IReadOnlyList<string>> ScopedRoles { get; init; }
        = ReadOnlyDictionary<Guid, IReadOnlyList<string>>.Empty;

    /// <summary>
    /// Permission codes granted by <see cref="ScopedRoles"/>, keyed by organization. Never part
    /// of <see cref="Permissions"/>; read through <see cref="PermissionsOn"/>.
    /// </summary>
    public IReadOnlyDictionary<Guid, IReadOnlyList<string>> ScopedPermissions { get; init; }
        = ReadOnlyDictionary<Guid, IReadOnlyList<string>>.Empty;

    /// <summary>Returns true if the user has the specified role (case-insensitive).</summary>
    public bool HasRole(string roleName)
        => Roles.Any(r => r.Equals(roleName, StringComparison.OrdinalIgnoreCase));

    /// <summary>Returns true if the user has the specified claim type (optionally matching a value).</summary>
    public bool HasClaim(string claimType, string? claimValue = null)
        => Claims.Any(c =>
            c.Type.Equals(claimType, StringComparison.OrdinalIgnoreCase) &&
            (claimValue is null || c.Value.Equals(claimValue, StringComparison.OrdinalIgnoreCase)));

    /// <summary>Returns true if the user has the specified permission code (case-insensitive).</summary>
    public bool HasPermission(string permissionCode)
        => Permissions.Any(p => p.Equals(permissionCode, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Role names the user holds on <paramref name="organizationId"/>: <see cref="Roles"/> for the
    /// user's own organization, otherwise only the roles assigned on that organization.
    /// </summary>
    public IReadOnlyList<string> RolesOn(Guid organizationId)
        => On(organizationId, Roles, ScopedRoles);

    /// <summary>
    /// Permission codes the user holds on <paramref name="organizationId"/>: <see cref="Permissions"/>
    /// for the user's own organization, otherwise only those of the roles assigned on that organization.
    /// </summary>
    public IReadOnlyList<string> PermissionsOn(Guid organizationId)
        => On(organizationId, Permissions, ScopedPermissions);

    /// <summary>Returns true if the user holds the specified role on the organization (case-insensitive).</summary>
    public bool HasRoleOn(Guid organizationId, string roleName)
        => RolesOn(organizationId).Any(r => r.Equals(roleName, StringComparison.OrdinalIgnoreCase));

    /// <summary>Returns true if the user holds the specified permission code on the organization (case-insensitive).</summary>
    public bool HasPermissionOn(Guid organizationId, string permissionCode)
        => PermissionsOn(organizationId).Any(p => p.Equals(permissionCode, StringComparison.OrdinalIgnoreCase));

    private IReadOnlyList<string> On(
        Guid organizationId,
        IReadOnlyList<string> home,
        IReadOnlyDictionary<Guid, IReadOnlyList<string>> scoped)
    {
        if (User.OrganizationId == organizationId)
            return home;

        return scoped.TryGetValue(organizationId, out var values) ? values : [];
    }
}
