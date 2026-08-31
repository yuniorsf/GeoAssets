using Microsoft.Identity.Client;

namespace GeoAssets.Server;

/// <summary>
/// Acquires app-only Microsoft Graph access tokens via MSAL.NET's client-credentials flow,
/// using the "GeoAssets Role Sync" service principal provisioned per XD01-60. Internal:
/// <see cref="EntraGraphRoleAssignmentProvider"/> depends on <see cref="IGraphAccessTokenProvider"/>,
/// not this concrete type, so tests substitute a fixed token instead of acquiring a real one.
///
/// <see cref="IConfidentialClientApplication"/> caches tokens in-memory for their lifetime
/// (MSAL.NET's default behavior), so repeated calls before expiry don't re-hit the token
/// endpoint.
/// </summary>
internal sealed class MsalGraphAccessTokenProvider : IGraphAccessTokenProvider
{
    private static readonly string[] GraphDefaultScope = ["https://graph.microsoft.com/.default"];

    private readonly IConfidentialClientApplication _app;

    public MsalGraphAccessTokenProvider(GraphCredentialOptions credential)
    {
        _app = ConfidentialClientApplicationBuilder
            .Create(credential.ClientId)
            .WithClientSecret(credential.ClientSecret)
            .WithTenantId(credential.TenantId)
            .Build();
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken ct = default)
    {
        var result = await _app.AcquireTokenForClient(GraphDefaultScope).ExecuteAsync(ct);
        return result.AccessToken;
    }
}
