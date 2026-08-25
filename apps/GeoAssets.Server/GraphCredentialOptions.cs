namespace GeoAssets.Server;

/// <summary>
/// The client-credentials shape <see cref="MsalGraphAccessTokenProvider"/> needs, extracted out
/// of <see cref="RoleSyncOptions"/> (XD01-62) so it isn't the only feature that can construct
/// one — <c>EntraGraphUserInvitationProvider</c> (XD01-67) reuses the exact same "GeoAssets Role
/// Sync" credential (XD01-65 extends that one app registration's permissions rather than
/// provisioning a second one) instead of standing up a second MSAL confidential-client instance.
/// </summary>
public sealed record GraphCredentialOptions(string TenantId, string ClientId, string ClientSecret);
