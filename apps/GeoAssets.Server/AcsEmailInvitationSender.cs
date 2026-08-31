using System.Net;
using Azure;
using Azure.Communication.Email;
using GeoAssets.Identity.Authorization.Services;

namespace GeoAssets.Server;

/// <summary>
/// <see cref="IInvitationEmailSender"/> implementation backed by Azure Communication Services
/// Email (XD01-59 Phase 3, XD01-68). Server-only.
///
/// Per this phase's confirmed simplification, the email links to the app's normal sign-in page
/// (<see cref="InvitationOptions.PublicWebAppUrl"/>) and instructs the invitee to use "Forgot
/// password?" — Entra's own Email-OTP SSPR flow handles verification, so no bespoke invitation
/// token/link is needed.
/// </summary>
public sealed class AcsEmailInvitationSender : IInvitationEmailSender
{
    private readonly EmailClient _client;
    private readonly AcsEmailOptions _acsOptions;
    private readonly InvitationOptions _invitationOptions;

    public AcsEmailInvitationSender(EmailClient client, AcsEmailOptions acsOptions, InvitationOptions invitationOptions)
    {
        _client            = client;
        _acsOptions        = acsOptions;
        _invitationOptions = invitationOptions;
    }

    public async Task SendInvitationAsync(string toEmail, string displayName, CancellationToken ct = default)
    {
        var signInUrl = _invitationOptions.PublicWebAppUrl;

        var plainText =
            $"Hi {displayName},\n\n" +
            "An account has been created for you on GeoAssets.\n\n" +
            $"Sign in here: {signInUrl}\n\n" +
            "On your first visit, click \"Forgot password?\" to set your password.\n";

        var encodedName = WebUtility.HtmlEncode(displayName);
        var html =
            $"<p>Hi {encodedName},</p>" +
            "<p>An account has been created for you on GeoAssets.</p>" +
            $"<p><a href=\"{signInUrl}\">Sign in here</a>.</p>" +
            "<p>On your first visit, click <strong>&quot;Forgot password?&quot;</strong> to set your password.</p>";

        var message = new EmailMessage(
            _acsOptions.FromAddress,
            toEmail,
            new EmailContent("You've been invited to GeoAssets") { PlainText = plainText, Html = html });

        await _client.SendAsync(WaitUntil.Completed, message, ct);
    }
}
