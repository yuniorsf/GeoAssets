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
    /// and <see cref="GeoAssetsInvitationEmailExtensions.AddInvitationEmailSender"/> register
    /// <see cref="GeoAssets.Identity.Authorization.Services.NullUserInvitationProvider"/> and
    /// <see cref="GeoAssets.Identity.Authorization.Services.NullInvitationEmailSender"/> instead.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// The app's normal sign-in page URL, linked from the invitation email (XD01-68). The
    /// invitee clicks "Forgot password?" there to set their password via Entra's own
    /// Email-OTP SSPR flow — no bespoke invitation token/link is needed.
    /// </summary>
    public string PublicWebAppUrl { get; set; } = string.Empty;
}
