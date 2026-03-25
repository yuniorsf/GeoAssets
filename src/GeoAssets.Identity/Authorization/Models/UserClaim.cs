namespace GeoAssets.Identity.Authorization.Models;

/// <summary>
/// Application-level claim stored in the database, assigned to a specific user.
///
/// These are separate from Azure AD JWT claims. They represent domain-specific
/// assertions that the application controls directly.
///
/// Examples:
///   Type="zone"        Value="north"
///   Type="department"  Value="operations"
///   Type="region"      Value="LatAm"
/// </summary>
public sealed class UserClaim
{
    public Guid   Id     { get; set; } = Guid.NewGuid();
    public Guid   UserId { get; set; }

    /// <summary>Claim type key (e.g. "zone", "department").</summary>
    public string Type   { get; set; } = string.Empty;

    /// <summary>Claim value (e.g. "north", "operations").</summary>
    public string Value  { get; set; } = string.Empty;

    // ── Navigation ────────────────────────────────────────────────────────────

    public AppUser User { get; set; } = null!;
}
