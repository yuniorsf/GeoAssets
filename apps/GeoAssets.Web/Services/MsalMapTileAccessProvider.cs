using GeoAssets.Identity.Authentication;
using GeoAssets.Shared.Interfaces;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;

namespace GeoAssets.Web.Services;

/// <summary>
/// The Web client's <see cref="IMapTileAccessProvider"/> (XD01-96): a WMS layer under <see cref="ApiScopeRoutes.BaseUrl"/>
/// (the gateway) gets a token for the scope <see cref="ApiScopeRoutes.ScopeFor"/> names for it, from the same MSAL
/// <see cref="IAccessTokenProvider"/> the <c>"GeoAssetsServer"</c> client's handlers use; a layer on any other host gets
/// no token. A token that can't be acquired silently is reported unavailable rather than starting a sign-in redirect
/// from a map tile.
/// </summary>
internal sealed class MsalMapTileAccessProvider : IMapTileAccessProvider
{
    private readonly IAccessTokenProvider _tokenProvider;
    private readonly ApiScopeRoutes _routes;

    public MsalMapTileAccessProvider(IAccessTokenProvider tokenProvider, ApiScopeRoutes routes)
    {
        ArgumentNullException.ThrowIfNull(tokenProvider);
        ArgumentNullException.ThrowIfNull(routes);
        _tokenProvider = tokenProvider;
        _routes = routes;
    }

    public async Task<MapTileAccess> GetAccessAsync(Uri tileUrl, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tileUrl);
        if (!tileUrl.IsAbsoluteUri || !_routes.BaseUrl.IsBaseOf(tileUrl))
            return MapTileAccess.Anonymous;

        if (_routes.ScopeFor(tileUrl) is not { } scope)
            return MapTileAccess.Unavailable;

        var result = await _tokenProvider
            .RequestAccessToken(new AccessTokenRequestOptions { Scopes = [scope] })
            .ConfigureAwait(false);
        return result.TryGetToken(out var token)
            ? MapTileAccess.Bearer(token.Value, token.Expires)
            : MapTileAccess.Unavailable;
    }
}
