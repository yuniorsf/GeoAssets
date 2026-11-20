using System.Net.Http.Headers;
using GeoAssets.Identity.Authentication;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;

namespace GeoAssets.Web.Services;

/// <summary>
/// The <c>"GeoAssetsServer"</c> client's token handler once a route scope is configured (XD01-397): a request under
/// <see cref="ApiScopeRoutes.BaseUrl"/> (the gateway) carries a token for the scope <see cref="ApiScopeRoutes.ScopeFor"/>
/// names for its path — one scope per token request, since a token request can carry only one resource's scopes. A
/// request outside the gateway, or one no scope is configured for, is sent without a token. A token that can't be
/// acquired silently throws <see cref="AccessTokenNotAvailableException"/>, the framework
/// <see cref="AuthorizationMessageHandler"/>'s contract.
/// </summary>
internal sealed class RouteScopedAuthorizationMessageHandler : DelegatingHandler
{
    private readonly IAccessTokenProvider _tokenProvider;
    private readonly NavigationManager _navigation;
    private readonly ApiScopeRoutes _routes;

    public RouteScopedAuthorizationMessageHandler(
        IAccessTokenProvider tokenProvider, NavigationManager navigation, ApiScopeRoutes routes)
    {
        ArgumentNullException.ThrowIfNull(tokenProvider);
        ArgumentNullException.ThrowIfNull(navigation);
        ArgumentNullException.ThrowIfNull(routes);
        _tokenProvider = tokenProvider;
        _navigation = navigation;
        _routes = routes;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var uri = request.RequestUri;
        if (uri is not null && uri.IsAbsoluteUri && _routes.BaseUrl.IsBaseOf(uri) && _routes.ScopeFor(uri) is { } scope)
        {
            string[] scopes = [scope];
            var result = await _tokenProvider
                .RequestAccessToken(new AccessTokenRequestOptions { Scopes = scopes })
                .ConfigureAwait(false);
            if (!result.TryGetToken(out var token))
                throw new AccessTokenNotAvailableException(_navigation, result, scopes);

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);
        }

        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
