using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Services;
using GeoAssets.MAUI.Services;
using GeoAssets.Shared.Interfaces;
using GeoAssets.Shared.Services;
using Microsoft.Extensions.Logging;

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
        builder.Services.AddSingleton<IAssetRepository, InMemoryAssetRepository>();
        builder.Services.AddScoped<IStorageService, FileStorageService>();
        builder.Services.AddScoped<IMapInterop, MapInteropService>();
        builder.Services.AddScoped<AssetService>();

        return builder.Build();
    }
}
