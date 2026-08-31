namespace GeoAssets.Server;

/// <summary>
/// Binds the <c>"AcsEmail"</c> configuration section (XD01-59 Phase 3) — the Azure Communication
/// Services Email resource provisioned per <c>InvitationAzureSetup.md</c>/XD01-65.
/// <see cref="AccessKey"/> must never live in a tracked appsettings.json — see the project's
/// public-repo CIAM secrets convention (<c>dotnet user-secrets</c> locally, the deployed secret
/// store in production).
/// </summary>
public sealed class AcsEmailOptions
{
    /// <summary>The ACS resource's endpoint, e.g. <c>https://YOUR_ACS_RESOURCE.communication.azure.com</c>.</summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>Access key for the above — never checked into source control.</summary>
    public string AccessKey { get; set; } = string.Empty;

    /// <summary>The verified sender address invitation emails are sent from.</summary>
    public string FromAddress { get; set; } = string.Empty;
}
