using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Services;
using GeoAssets.Core.Providers;
using GeoAssets.Provider.PostgreSQL;
using GeoAssets.MAUI.Services;
using GeoAssets.Shared.Interfaces;
using GeoAssets.Shared.Services;
using Microsoft.Extensions.Configuration;
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

        // Load appsettings.json embedded into the assembly
        using var configStream = typeof(MauiProgram).Assembly
            .GetManifestResourceStream("GeoAssets.MAUI.appsettings.json");
        if (configStream is not null)
            builder.Configuration.AddJsonStream(configStream);

#pragma warning disable CA1416 // Validate platform compatibility
        builder.Services.AddMauiBlazorWebView();
#pragma warning restore CA1416 // Validate platform compatibility

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        // GeoAssets services
        builder.Services.AddSingleton<IProviderPool, ProviderPool>();
        builder.Services.AddSingleton<ActiveAssetProvider>();
        builder.Services.AddSingleton<IAssetProvider>(sp => sp.GetRequiredService<ActiveAssetProvider>());
        builder.Services.AddGeoAssetsPostgres();
        builder.Services.AddScoped<IStorageService, FileStorageService>();
        builder.Services.Configure<MapInteropOptions>(
            builder.Configuration.GetSection("MapInterop"));
        builder.Services.AddScoped<MapInteropService>();
        builder.Services.AddScoped<IMapInterop>(sp => sp.GetRequiredService<MapInteropService>());
        builder.Services.AddScoped<AssetService>();

        return builder.Build();
    }
}
