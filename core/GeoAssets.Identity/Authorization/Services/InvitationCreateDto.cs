namespace GeoAssets.Identity.Authorization.Services;

/// <summary>Admin write payload to create an invitation.</summary>
public sealed record InvitationCreateDto(string Email, string DisplayName);
