namespace GeoAssets.Identity.Authorization.Models;

/// <summary>Join entity: many-to-many between <see cref="AppUser"/> and <see cref="AppRole"/>.</summary>
public sealed class UserRole
{
    public Guid UserId   { get; set; }
    public Guid RoleId   { get; set; }

    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
    public string?  AssignedBy { get; set; }

    // ── Navigation ────────────────────────────────────────────────────────────

    public AppUser User { get; set; } = null!;
    public AppRole Role { get; set; } = null!;
}
