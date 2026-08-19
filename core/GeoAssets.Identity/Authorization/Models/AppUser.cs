namespace GeoAssets.Identity.Authorization.Models;

/// <summary>
/// Application user stored in the local database.
/// Linked to the external IdP via <see cref="ExternalObjectId"/> (the `oid` JWT claim, or its
/// equivalent for other providers — XD01-51).
///
/// Users are provisioned on first login (Just-In-Time provisioning):
/// when the IdP authenticates a user, the app looks up or creates an <see cref="AppUser"/>
/// matching the ExternalObjectId.
/// </summary>
public sealed class AppUser
{
    public Guid     Id               { get; set; } = Guid.NewGuid();

    /// <summary>The IdP's stable object identifier for this user (`oid` claim or equivalent).</summary>
    public string   ExternalObjectId { get; set; } = string.Empty;

    public string   Email            { get; set; } = string.Empty;
    public string   DisplayName      { get; set; } = string.Empty;
    public bool     IsActive         { get; set; } = true;

    public required DateTime CreatedAt { get; set; }
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
