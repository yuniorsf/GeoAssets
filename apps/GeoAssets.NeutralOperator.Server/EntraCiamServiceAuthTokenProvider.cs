using GeoAssets.Identity.Authentication;
using Microsoft.Identity.Client;

namespace GeoAssets.NeutralOperator.Server;

/// <summary>
/// App-only bearer tokens through MSAL.NET's client-credentials flow against the GeoAssets Entra CIAM tenant, so this host
/// calls the kernel as itself (XD01-299) — the same provider as <c>GeoAssets.FieldService.Server</c>'s. MSAL caches each
/// token for its lifetime, so asking on every request is cheap.
/// </summary>
internal sealed class EntraCiamServiceAuthTokenProvider : IServiceAuthTokenProvider
{
    private readonly IConfidentialClientApplication _app;
    private readonly string[] _scopes;

    public EntraCiamServiceAuthTokenProvider(ServiceAuthOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _app = ConfidentialClientApplicationBuilder
            .Create(options.ClientId)
            .WithClientSecret(options.ClientSecret)
            .WithTenantId(options.TenantId)
            .Build();
        _scopes = [options.Scope];
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        var result = await _app.AcquireTokenForClient(_scopes)
            .ExecuteAsync(cancellationToken)
            .ConfigureAwait(false);
        return result.AccessToken;
    }
}
