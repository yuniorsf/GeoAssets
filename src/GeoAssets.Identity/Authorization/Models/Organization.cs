namespace GeoAssets.Identity.Authorization.Models;

/// <summary>
/// Represents a tenant / organizational unit that groups users.
///
/// A user belongs to exactly one organization. Roles and permissions
/// are still global (not scoped per-org) — extend if multi-tenancy
/// with per-org policies is required.
/// </summary>
public sealed class Organization
{
    public Guid    Id          { get; set; } = Guid.NewGuid();

    /// <summary>Human-readable display name (e.g. "Empresa Eléctrica del Norte").</summary>
    public string  Name        { get; set; } = string.Empty;

    /// <summary>Short URL-safe identifier (e.g. "een"). Unique.</summary>
    public string  Slug        { get; set; } = string.Empty;

    public string? Description { get; set; }
    public bool    IsActive    { get; set; } = true;

    public DateTime  CreatedAt { get; set; } = DateTime.UtcNow;

    // ── Navigation ────────────────────────────────────────────────────────────

    public List<AppUser> Users { get; set; } = [];
}
