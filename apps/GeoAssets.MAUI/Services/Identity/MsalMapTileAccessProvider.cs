using GeoAssets.Identity.Authentication;
using GeoAssets.Shared.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client;

namespace GeoAssets.MAUI.Services.Identity;

/// <summary>
/// MAUI's <see cref="IMapTileAccessProvider"/> (XD01-96): a WMS layer under <see cref="ApiScopeRoutes.BaseUrl"/> gets a
/// token for the scope <see cref="ApiScopeRoutes.ScopeFor"/> names for it, acquired silently through MSAL.NET for the
/// signed-in account, as <see cref="MsalAuthorizationHandler"/> does; a layer on any other host gets no token. With no
/// account, or when MSAL needs user interaction, the token is reported unavailable.
/// </summary>
public sealed partial class MsalMapTileAccessProvider(
    IPublicClientApplication app,
    ApiScopeRoutes routes,
    ILogger<MsalMapTileAccessProvider> logger) : IMapTileAccessProvider
{
    public async Task<MapTileAccess> GetAccessAsync(Uri tileUrl, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tileUrl);
        if (!tileUrl.IsAbsoluteUri || !routes.BaseUrl.IsBaseOf(tileUrl))
            return MapTileAccess.Anonymous;

        if (routes.ScopeFor(tileUrl) is not { } scope)
            return MapTileAccess.Unavailable;

        var account = (await app.GetAccountsAsync().ConfigureAwait(false)).FirstOrDefault();
        if (account is null)
            return MapTileAccess.Unavailable;

        try
        {
            var result = await app.AcquireTokenSilent([scope], account).ExecuteAsync(cancellationToken).ConfigureAwait(false);
            return MapTileAccess.Bearer(result.AccessToken, result.ExpiresOn);
        }
        catch (MsalUiRequiredException ex)
        {
            LogSilentAcquisitionFailed(logger, scope, ex);
            return MapTileAccess.Unavailable;
        }
    }

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "Silent token acquisition failed for '{Scope}' — WMS tiles from this app's server stay empty")]
    private static partial void LogSilentAcquisitionFailed(ILogger logger, string scope, Exception ex);
}
