using Microsoft.Identity.Client;

namespace GeoAssets.Tools.NetworkImport;

/// <summary>
/// App-only tokens through MSAL.NET's client-credentials flow, like <c>GeoAssets.NeutralOperator.Server</c>'s
/// <c>EntraCiamServiceAuthTokenProvider</c>: the tool calls the kernel as a service caller. MSAL caches each token for
/// its lifetime, so asking before every request is cheap.
/// </summary>
internal sealed class ClientCredentialsAccessTokenSource : IAccessTokenSource
{
    private readonly IConfidentialClientApplication _app;
    private readonly string[] _scopes;

    public ClientCredentialsAccessTokenSource(string tenantId, string clientId, string clientSecret, string scope)
    {
        _app = ConfidentialClientApplicationBuilder
            .Create(clientId)
            .WithClientSecret(clientSecret)
            .WithTenantId(tenantId)
            .Build();
        _scopes = [scope];
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await _app.AcquireTokenForClient(_scopes)
                .ExecuteAsync(cancellationToken)
                .ConfigureAwait(false);
            return result.AccessToken;
        }
        catch (MsalException exception)
        {
            throw new KernelFailureException($"Could not acquire a service token: {exception.Message}", exception);
        }
    }
}
