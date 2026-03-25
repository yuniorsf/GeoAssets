namespace GeoAssets.Identity.Authorization.Models;

/// <summary>Join table: a user's membership in a group.</summary>
public sealed class UserGroup
{
    public Guid     UserId   { get; set; }
    public Guid     GroupId  { get; set; }

    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
    public string?  AddedBy  { get; set; }

    // ── Navigation ────────────────────────────────────────────────────────────

    public AppUser  User  { get; set; } = null!;
    public AppGroup Group { get; set; } = null!;
}
