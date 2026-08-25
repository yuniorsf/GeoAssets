namespace GeoAssets.Identity.Authorization.Models;

/// <summary>Lifecycle state of a <see cref="PendingInvitation"/>.</summary>
public enum InvitationStatus
{
    /// <summary>Sent, not yet redeemed or revoked.</summary>
    Pending,

    /// <summary>The invited user completed first login and was matched via <see cref="PendingInvitation.ExternalObjectId"/>.</summary>
    Redeemed,

    /// <summary>Revoked before redemption; the invited user can no longer complete sign-up.</summary>
    Revoked
}
