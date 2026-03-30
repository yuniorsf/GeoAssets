using GeoAssets.Core.Interfaces;
using GeoAssets.Provider.Active;
using GeoAssets.Provider.InMemory;
using GeoAssets.Provider.PostgreSQL;
using GeoAssets.MAUI.Services;
using GeoAssets.Shared.Interfaces;
using GeoAssets.Shared.Services;
using Microsoft.Extensions.Logging;
using GeoAssets.Core.Services;

namespace GeoAssets.MAUI;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            });

        builder.Services.AddMauiBlazorWebView();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        // GeoAssets services
        builder.Services.AddSingleton<IProviderPool, InMemoryProviderPool>();
        builder.Services.AddSingleton<ActiveAssetProvider>();
        builder.Services.AddSingleton<IAssetProvider>(sp => sp.GetRequiredService<ActiveAssetProvider>());
        builder.Services.AddGeoAssetsPostgres();
        builder.Services.AddScoped<IStorageService, FileStorageService>();
        builder.Services.AddScoped<IMapInterop, MapInteropService>();
        builder.Services.AddScoped<AssetService>();

        return builder.Build();
    }
}
