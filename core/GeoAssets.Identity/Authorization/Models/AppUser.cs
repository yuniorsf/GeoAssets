namespace GeoAssets.Identity.Authorization.Models;

/// <summary>
/// Application user stored in the local database.
/// Linked to Azure AD via <see cref="AzureObjectId"/> (the `oid` JWT claim).
///
/// Users are provisioned on first login (Just-In-Time provisioning):
/// when Azure AD authenticates a user, the app looks up or creates an <see cref="AppUser"/>
/// matching the AzureObjectId.
/// </summary>
public sealed class AppUser
{
    public Guid     Id            { get; set; } = Guid.NewGuid();

    /// <summary>Azure AD Object ID (`oid` claim) — the stable external identifier.</summary>
    public string   AzureObjectId { get; set; } = string.Empty;

    public string   Email         { get; set; } = string.Empty;
    public string   DisplayName   { get; set; } = string.Empty;
    public bool     IsActive      { get; set; } = true;

    public DateTime  CreatedAt    { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt  { get; set; }

    // ── Organization ──────────────────────────────────────────────────────────

    /// <summary>
    /// The organization this user belongs to.
    /// Nullable so existing users are not broken before an org is assigned.
    /// </summary>
    public Guid? OrganizationId { get; set; }

    // ── Navigation ────────────────────────────────────────────────────────────

    public Organization?   Organization { get; set; }
    public List<UserRole>  UserRoles    { get; set; } = [];
    public List<UserClaim> UserClaims   { get; set; } = [];
    public List<UserGroup> UserGroups   { get; set; } = [];
}
