using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Identity.Client;

namespace GeoAssets.MAUI.Services.Identity;

/// <summary>
/// <see cref="AuthenticationStateProvider"/> for GeoAssets.MAUI (XD01-52), backed by MSAL.NET's
/// <see cref="IPublicClientApplication"/> instead of Blazor WebAssembly's browser-redirect flow —
/// there's no URL to redirect to in a native app, so sign-in/out are direct method calls
/// (<see cref="SignInInteractiveAsync"/>/<see cref="SignOutAsync"/>) rather than navigation.
/// <see cref="MauiAuthNavigationService"/> exposes those as the shared <c>IAuthNavigationService</c>
/// contract; <see cref="MauiCurrentUserAccessor"/> reads the resulting state via
/// <see cref="GetAuthenticationStateAsync"/>, same as Blazor WASM's
/// <c>BlazorWasmCurrentUserAccessor</c> does against its own <see cref="AuthenticationStateProvider"/>.
///
/// <see cref="AuthenticationResult.ClaimsPrincipal"/> already builds a <see cref="ClaimsPrincipal"/>
/// from the ID token's claims, so no manual JWT parsing is needed here.
/// </summary>
public sealed class MauiMsalAuthenticationStateProvider(
    IPublicClientApplication app, IConfiguration configuration) : AuthenticationStateProvider
{
    private static readonly AuthenticationState Anonymous = new(new ClaimsPrincipal(new ClaimsIdentity()));

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var account = (await app.GetAccountsAsync()).FirstOrDefault();
        if (account is null) return Anonymous;

        try
        {
            var result = await app.AcquireTokenSilent(RequestedScopes, account).ExecuteAsync();
            return new AuthenticationState(result.ClaimsPrincipal);
        }
        catch (MsalUiRequiredException)
        {
            // Cached account's tokens are expired/revoked and can't be silently refreshed —
            // treat as signed out rather than throwing; the user must sign in interactively again.
            return Anonymous;
        }
    }

    /// <summary>Launches MSAL's interactive (system-browser) sign-in flow.</summary>
    public async Task SignInInteractiveAsync(CancellationToken ct = default)
    {
        var result = await app.AcquireTokenInteractive(RequestedScopes).ExecuteAsync(ct);
        NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(result.ClaimsPrincipal)));
    }

    /// <summary>Removes all cached MSAL accounts and notifies subscribers of the anonymous state.</summary>
    public async Task SignOutAsync(CancellationToken ct = default)
    {
        foreach (var account in await app.GetAccountsAsync())
            await app.RemoveAsync(account);

        NotifyAuthenticationStateChanged(Task.FromResult(Anonymous));
    }

    // "openid"/"profile"/"offline_access" are valid for any Entra app registration with zero
    // extra consent, so silent/interactive acquisition always has something to request even
    // before GeoAssetsServer:ApiScope is configured with a real value.
    private IEnumerable<string> RequestedScopes
    {
        get
        {
            List<string> scopes = ["openid", "profile", "offline_access"];
            var apiScope = configuration["GeoAssetsServer:ApiScope"];
            if (!string.IsNullOrWhiteSpace(apiScope))
                scopes.Add(apiScope);
            return scopes;
        }
    }
}
