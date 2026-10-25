namespace GeoAssets.Identity.Authorization.Services;

/// <summary>
/// Admin write payload for a role. <c>IsBuiltIn</c> is deliberately absent — it's
/// server-controlled (always <see langword="false"/> for admin-created roles).
/// </summary>
public sealed record RoleWriteDto(
    string Name,
    string Description);
