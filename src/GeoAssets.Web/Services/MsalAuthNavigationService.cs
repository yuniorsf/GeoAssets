using GeoAssets.Shared.Interfaces;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;

namespace GeoAssets.Web.Services;

/// <summary>
/// MSAL-backed implementation of <see cref="IAuthNavigationService"/>.
/// Uses the proper SignOutSessionStateManager flow so RemoteAuthenticatorView
/// accepts the logout request without the "not initiated from within the page" error.
/// </summary>
public sealed class MsalAuthNavigationService(NavigationManager nav) : IAuthNavigationService
{
    public void NavigateToLogin(string returnUrl = "/")
        => nav.NavigateToLogin($"authentication/login?returnUrl={Uri.EscapeDataString(returnUrl)}");

    public void NavigateToLogout(string returnUrl = "/login")
        => nav.NavigateToLogout("authentication/logout", returnUrl);
}
