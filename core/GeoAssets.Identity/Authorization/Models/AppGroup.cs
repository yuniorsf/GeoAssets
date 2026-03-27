namespace GeoAssets.Identity.Authorization.Models;

/// <summary>
/// A named collection of users within an organization.
///
/// Groups are used for bulk dispatch of service orders and for
/// permission grants that apply to all members simultaneously.
/// A user can belong to multiple groups.
/// </summary>
public sealed class AppGroup
{
    public Guid    Id             { get; set; } = Guid.NewGuid();
    public string  Name           { get; set; } = string.Empty;
    public string? Description    { get; set; }

    /// <summary>Optional scope: if set, the group only makes sense inside this org.</summary>
    public Guid?   OrganizationId { get; set; }

    public bool     IsActive  { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // ── Navigation ────────────────────────────────────────────────────────────

    public Organization?    Organization { get; set; }
    public List<UserGroup>  UserGroups   { get; set; } = [];
}
