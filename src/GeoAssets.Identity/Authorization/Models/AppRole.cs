namespace GeoAssets.Identity.Authorization.Models;

/// <summary>
/// Application role stored in the local database.
///
/// Roles group a set of permissions and can be assigned to users.
/// Examples: "FieldTechnician", "Supervisor", "ReadOnly", "Administrator".
///
/// Note: Azure AD App Roles (from the JWT token) are distinct from these DB roles.
/// Both can be checked independently in policies.
/// </summary>
public sealed class AppRole
{
    public Guid   Id          { get; set; } = Guid.NewGuid();
    public string Name        { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    /// <summary>Built-in roles cannot be deleted by users.</summary>
    public bool   IsBuiltIn   { get; set; }

    // ── Navigation ────────────────────────────────────────────────────────────

    public List<UserRole>       UserRoles       { get; set; } = [];
    public List<RolePermission> RolePermissions { get; set; } = [];
}
