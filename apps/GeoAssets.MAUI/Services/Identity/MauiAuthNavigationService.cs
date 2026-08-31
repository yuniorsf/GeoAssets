using GeoAssets.Shared.Interfaces;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace GeoAssets.MAUI.Services.Identity;

/// <summary>
/// <see cref="IAuthNavigationService"/> for GeoAssets.MAUI (XD01-52). Unlike
/// <c>BlazorRemoteAuthNavigationService</c> (Web), which navigates to a URL that a separate
/// remote-auth handler intercepts, a native app has no such URL — "navigating to login/logout"
/// here means invoking MSAL directly through <see cref="MauiMsalAuthenticationStateProvider"/>.
///
/// Fire-and-forget with logging on failure, matching this codebase's existing idiom for
/// synchronous event-handler-shaped methods wrapping async work (see
/// <c>ProviderConnectionMapRenderer.OnEntryAdded</c>). The MAUI login page itself calls
/// <see cref="MauiMsalAuthenticationStateProvider.SignInInteractiveAsync"/> directly instead of
/// going through <see cref="NavigateToLogin"/>, so it can await the result and show a specific
/// error message — this implementation exists for the shared <see cref="IAuthNavigationService"/>
/// contract (e.g. TopBar's sign-out button), where no such UI feedback is expected.
/// </summary>
public sealed class MauiAuthNavigationService(
    MauiMsalAuthenticationStateProvider authStateProvider,
    NavigationManager navigationManager,
    ILogger<MauiAuthNavigationService> logger) : IAuthNavigationService
{
    public void NavigateToLogin(string returnUrl = "/") => _ = SignInAndNavigateAsync(returnUrl);

    public void NavigateToLogout(string returnUrl = "/login") => _ = SignOutAndNavigateAsync(returnUrl);

    private async Task SignInAndNavigateAsync(string returnUrl)
    {
        try
        {
            await authStateProvider.SignInInteractiveAsync();
            navigationManager.NavigateTo(returnUrl);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Interactive MSAL sign-in failed");
        }
    }

    private async Task SignOutAndNavigateAsync(string returnUrl)
    {
        try
        {
            await authStateProvider.SignOutAsync();
            navigationManager.NavigateTo(returnUrl);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "MSAL sign-out failed");
        }
    }
}
