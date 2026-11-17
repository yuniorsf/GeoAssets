namespace GeoAssets.Identity.Authorization.Models;

/// <summary>
/// A role assignment: <see cref="AppUser"/> holds <see cref="AppRole"/>, either in the user's home
/// organization or on one other organization (XD01-325).
/// </summary>
public sealed class UserRole
{
    public Guid Id       { get; set; } = Guid.NewGuid();

    public Guid UserId   { get; set; }
    public Guid RoleId   { get; set; }

    /// <summary>
    /// The organization the role is held on. <c>null</c> means the assignment applies in the
    /// user's home organization; a value means "role held on that organization" and grants
    /// nothing in the home organization or anywhere else. A user holds a given role at most once
    /// per scope.
    /// </summary>
    public Guid? OrganizationId { get; set; }

    public required DateTime AssignedAt { get; set; }
    public string?  AssignedBy { get; set; }

    // ── Navigation ────────────────────────────────────────────────────────────

    public AppUser User { get; set; } = null!;
    public AppRole Role { get; set; } = null!;
    public Organization? Organization { get; set; }
}
