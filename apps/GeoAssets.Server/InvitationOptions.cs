namespace GeoAssets.Server;

/// <summary>
/// Binds the <c>"Invitation"</c> configuration section (XD01-59 Phase 3). Deliberately carries
/// no credential of its own — <see cref="EntraGraphUserInvitationProvider"/> reuses the same
/// "GeoAssets Role Sync" Graph credential as <see cref="RoleSyncOptions"/> (extended with the
/// extra permissions <c>InvitationAzureSetup.md</c>/XD01-65 provisions), read from the
/// <c>"RoleSync"</c> section, not duplicated here.
/// </summary>
public sealed class InvitationOptions
{
    /// <summary>
    /// Master switch. When false (the default), <see cref="GeoAssetsUserInvitationExtensions.AddUserInvitationProvider"/>
    /// registers <see cref="GeoAssets.Identity.Authorization.Services.NullUserInvitationProvider"/> instead.
    /// </summary>
    public bool Enabled { get; set; }
}
