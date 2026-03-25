namespace GeoAssets.Identity.Authorization.Models;

/// <summary>
/// Fine-grained permission representing a single action on a resource.
///
/// Convention: <c>{resource}:{action}</c>
/// Examples:
///   "serviceorders:create"   "serviceorders:assign"   "serviceorders:complete"
///   "features:read"          "features:delete"
///   "users:manage"           "reports:export"
///
/// Permissions are granted to roles, not directly to users.
/// A user's effective permissions are the union of all permissions from all their assigned roles.
/// </summary>
public sealed class AppPermission
{
    public Guid   Id          { get; set; } = Guid.NewGuid();

    /// <summary>Unique code used in policy evaluation. Format: <c>resource:action</c>.</summary>
    public string Code        { get; set; } = string.Empty;

    /// <summary>Resource category (e.g. "serviceorders", "features", "users").</summary>
    public string Resource    { get; set; } = string.Empty;

    /// <summary>Action within the resource (e.g. "create", "read", "delete").</summary>
    public string Action      { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    // ── Navigation ────────────────────────────────────────────────────────────

    public List<RolePermission> RolePermissions { get; set; } = [];
}
