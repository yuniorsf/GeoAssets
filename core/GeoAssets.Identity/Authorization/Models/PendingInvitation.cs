namespace GeoAssets.Identity.Authorization.Models;

/// <summary>
/// Tracks an invite-only registration's own lifecycle — deliberately minimal, no role/org
/// fields, since role assignment happens after first login via the already-shipped XD01-63 UI,
/// not at invite time.
/// </summary>
public sealed class PendingInvitation
{
    public Guid   Id    { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// The Graph <c>POST /users</c> response's object id — the same value <see cref="AppUser.ExternalObjectId"/>
    /// gets on first login, used as the JIT-provisioning match key.
    /// </summary>
    public string ExternalObjectId { get; set; } = string.Empty;

    public Guid     InvitedByUserId { get; set; }
    public DateTime InvitedAt       { get; set; }
    public DateTime? RedeemedAt     { get; set; }

    public InvitationStatus Status { get; set; }
}
