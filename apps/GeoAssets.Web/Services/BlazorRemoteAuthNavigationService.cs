using GeoAssets.Shared.Interfaces;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;

namespace GeoAssets.Web.Services;

/// <summary>
/// <see cref="IAuthNavigationService"/> implementation for the Blazor WebAssembly host
/// (XD01-50). Wraps <c>NavigationManager</c>'s remote-authentication extensions
/// (<c>Microsoft.AspNetCore.Components.WebAssembly.Authentication</c>) — these are part of the
/// generic RemoteAuthenticatorView flow, not specific to any one
/// <see cref="GeoAssets.Identity.Authentication.IGeoAuthenticationProvider"/> vendor (XD01-48),
/// so this class is shared regardless of which CIAM/OIDC vendor is configured.
/// Uses the proper SignOutSessionStateManager flow so RemoteAuthenticatorView accepts the
/// logout request without the "not initiated from within the page" error.
/// </summary>
public sealed class BlazorRemoteAuthNavigationService(NavigationManager nav) : IAuthNavigationService
{
    public void NavigateToLogin(string returnUrl = "/")
        => nav.NavigateToLogin($"authentication/login?returnUrl={Uri.EscapeDataString(returnUrl)}");

    public void NavigateToLogout(string returnUrl = "/login")
        => nav.NavigateToLogout("authentication/logout", returnUrl);
}
