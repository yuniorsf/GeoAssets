using GeoAssets.Identity.Authorization.Models;

namespace GeoAssets.Identity.Authorization.Services;

/// <summary>Wire shape for a <c>PendingInvitation</c> row (XD01-69).</summary>
public sealed record PendingInvitationDto(
    Guid             Id,
    string           Email,
    string           ExternalObjectId,
    Guid             InvitedByUserId,
    DateTime         InvitedAt,
    DateTime?        RedeemedAt,
    InvitationStatus Status);
