namespace GeoAssets.Identity.Authorization.Services;

/// <summary>Wire shape for <c>GET /api/identity/rolesync/status</c> (XD01-63).</summary>
public sealed record RoleSyncStatusDto(bool Enabled);
