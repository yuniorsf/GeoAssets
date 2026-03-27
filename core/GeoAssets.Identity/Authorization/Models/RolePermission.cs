namespace GeoAssets.Identity.Authorization.Models;

/// <summary>Join entity: many-to-many between <see cref="AppRole"/> and <see cref="AppPermission"/>.</summary>
public sealed class RolePermission
{
    public Guid RoleId       { get; set; }
    public Guid PermissionId { get; set; }

    // ── Navigation ────────────────────────────────────────────────────────────

    public AppRole       Role       { get; set; } = null!;
    public AppPermission Permission { get; set; } = null!;
}
