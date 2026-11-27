using GeoAssets.Core.Interfaces;
using GeoAssets.MAUI.Services;
using GeoAssets.MAUI.Services.Projects;
using GeoAssets.Shared.Services;
using GeoAssets.Shared.Theming;
using Microsoft.Extensions.DependencyInjection;

namespace GeoAssets.MAUI.Extensions;

/// <summary>
/// The services the shared <c>MainLayout</c> — the map page's, the assets table's, the service orders' and the admin
/// pages' layout — its top bar and its sidebar panels inject, which only the Web registered before XD01-476:
/// <list type="bullet">
///   <item><see cref="IBootLoader"/>: the Web's <see cref="BootLoaderService"/> as it is — the first-run provider dialog
///   connects through the app's plugin registry and pool, and keeps the choice in the app's <see cref="IStorageService"/>
///   (its data folder).</item>
///   <item><see cref="IProjectSessionService"/>/<see cref="IProjectClient"/>: no Project is ever open
///   (<see cref="NoProjectSessionService"/> says why), and <see cref="IProjectAutosaveService"/> over it never has one to
///   save.</item>
///   <item><see cref="Core.Theming.IThemeService"/>: the Web's theme service; the WebView has the same <c>localStorage</c> and
///   <c>data-bs-theme</c> the inline script in <c>index.html</c> already resolves.</item>
///   <item><see cref="InvitationRedirectGate"/>: resolved by the layout on every load; with no
///   <c>IPendingInvitationRepository</c> here it never redirects — as before, without the layout logging its absence.</item>
///   <item><see cref="IAnalyticsService"/>: the data sources panel's events, dropped (<see cref="NoAnalyticsService"/>).</item>
/// </list>
/// </summary>
internal static class MauiMainLayoutServiceExtensions
{
    public static IServiceCollection AddMauiMainLayoutServices(this IServiceCollection services)
    {
        services.AddScoped<IBootLoader, BootLoaderService>();
        services.AddSingleton<IProjectClient, NoProjectClient>();
        services.AddScoped<IProjectSessionService, NoProjectSessionService>();
        services.AddScoped<IProjectAutosaveService, ProjectAutosaveService>();
        services.AddGeoAssetsTheming();
        services.AddScoped<InvitationRedirectGate>();
        services.AddSingleton<IAnalyticsService, NoAnalyticsService>();
        return services;
    }
}
