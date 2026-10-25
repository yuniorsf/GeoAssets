namespace GeoAssets.Identity.Authorization.Services;

/// <summary>Wire shape for <c>GET /api/identity/invitations/status</c> (XD01-69).</summary>
public sealed record InvitationStatusDto(bool Enabled);
