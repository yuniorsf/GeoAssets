namespace GeoAssets.Shared.Interfaces;

/// <summary>
/// Abstracts host-specific authentication navigation (login / logout).
/// Implemented by each host (Web = Blazor WebAssembly remote-auth, MAUI = native auth).
/// </summary>
public interface IAuthNavigationService
{
    void NavigateToLogin(string returnUrl = "/");
    void NavigateToLogout(string returnUrl = "/login");
}
