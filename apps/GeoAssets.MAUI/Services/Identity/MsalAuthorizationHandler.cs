using System.Net.Http.Headers;
using GeoAssets.Identity.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client;

namespace GeoAssets.MAUI.Services.Identity;

/// <summary>
/// Attaches a bearer access token (acquired silently via MSAL) to outgoing requests, mirroring
/// the <c>AuthorizationMessageHandler</c> pattern in apps/GeoAssets.Web/Program.cs's
/// "GeoAssetsServer" HttpClient — adapted for MSAL.NET's own account/token cache instead of
/// Blazor WASM's <c>AuthorizationMessageHandler</c> (XD01-52).
///
/// No caller registers the "GeoAssetsServer" named HttpClient against a real endpoint yet — MAUI
/// has no REST-backed provider/repository today (it's InMemory + PostgreSQL only). Registered
/// ahead of that, same as <c>ProviderConnectionMapRenderer</c> was in XD01-83 before XD01-84 wired
/// it in: the seam is ready for whenever MAUI gains a REST call site against GeoAssets.Server.
///
/// The token's one scope is <see cref="ApiScopeRoutes.ScopeFor"/>'s for the request (XD01-397): a
/// <c>GeoAssetsServer:RouteScopes</c> entry's under that route on the gateway, else <c>GeoAssetsServer:ApiScope</c>.
/// </summary>
public sealed partial class MsalAuthorizationHandler(
    IPublicClientApplication app,
    ApiScopeRoutes routes,
    ILogger<MsalAuthorizationHandler> logger) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var scope = request.RequestUri is { IsAbsoluteUri: true } uri ? routes.ScopeFor(uri) : routes.DefaultScope;
        if (!string.IsNullOrWhiteSpace(scope))
        {
            var account = (await app.GetAccountsAsync()).FirstOrDefault();
            if (account is not null)
            {
                try
                {
                    var result = await app.AcquireTokenSilent([scope], account).ExecuteAsync(cancellationToken);
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", result.AccessToken);
                }
                catch (MsalUiRequiredException ex)
                {
                    LogSilentAcquisitionFailed(logger, scope, ex);
                }
            }
        }

        return await base.SendAsync(request, cancellationToken);
    }

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "Silent token acquisition failed for '{Scope}' — sending request unauthenticated")]
    private static partial void LogSilentAcquisitionFailed(ILogger logger, string scope, Exception ex);
}
