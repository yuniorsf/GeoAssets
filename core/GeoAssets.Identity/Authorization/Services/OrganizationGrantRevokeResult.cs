namespace GeoAssets.Identity.Authorization.Services;

/// <summary>
/// Answer of <c>POST {prefix}/organization-grants/revoke</c> (XD01-385): how many active grants with the key were
/// deactivated — 0 when none was active, which is not an error (the call is idempotent).
/// </summary>
public sealed record OrganizationGrantRevokeResult(int Revoked);
