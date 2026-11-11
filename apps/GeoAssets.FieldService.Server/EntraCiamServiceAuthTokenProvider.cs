using GeoAssets.Identity.Authentication;
using Microsoft.Identity.Client;

namespace GeoAssets.FieldService.Server;

/// <summary>
/// Acquires app-only bearer tokens via MSAL.NET's client-credentials flow against the GeoAssets
/// Entra CIAM tenant, so this host can call another GeoAssets host (e.g. <c>GeoAssets.Server</c>)
/// authenticated as itself (XD01-299) — the service-identity counterpart to
/// <c>MsalGraphAccessTokenProvider</c>'s Graph token acquisition pattern already used elsewhere
/// in this system. <see cref="IConfidentialClientApplication"/> caches tokens in-memory for
/// their lifetime (MSAL.NET's default behavior), so callers can call
/// <see cref="GetAccessTokenAsync"/> on every outbound request without re-acquiring each time.
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
