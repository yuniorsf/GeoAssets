using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Services;
using GeoAssets.Core.Providers;
using GeoAssets.Identity.Authentication;
using GeoAssets.MAUI.Extensions;
using GeoAssets.MAUI.Services.Identity;
using GeoAssets.Provider.PostgreSQL;
using GeoAssets.MAUI.Services;
using GeoAssets.Shared.Interfaces;
using GeoAssets.Shared.Services;
using GeoAssets.Shared.Services.Observability;
using Microsoft.AspNetCore.Components.Authorization;
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
        //
        // Observability (XD01-43): wrapped with the same Observable* decorators as
        // apps/GeoAssets.Web/Program.cs, giving MAUI the same import/render/repository-timing
        // telemetry Blazor Web already has. Deliberately NOT wired to AddGeoAssetsObservability
        // (GeoAssets.Infrastructure.Observability) or an OTLP exporter here:
        //   - That extension is ASP.NET Core-oriented (FrameworkReference on
        //     Microsoft.AspNetCore.App, ASP.NET Core request instrumentation, a /healthz
        //     endpoint) — none of it applies to a MAUI client, which hosts no HTTP server.
        //   - A mobile/desktop app is a distributed binary an end user's device — unlike
        //     GeoAssets.Server, a vendor OTLP endpoint/API-key credential embedded in it can be
        //     extracted by decompiling the package. The safe pattern for client telemetry is
        //     proxying through a trusted backend, not exporting directly from the client; that's
        //     a separate, larger design (out of scope here).
        // The decorators below are safe with no exporter at all — ImportDiagnostics'
        // ActivitySource/Meter are zero-cost no-ops with nothing listening, exactly like
        // Blazor Web before OTel is wired up client-side.
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<IProviderPool, ProviderPool>();

        // Cross-cutting panel state (XD01-82/83) — same self-sufficient-DI wiring as
        // apps/GeoAssets.Web/Program.cs (XD01-84).
        builder.Services.AddGeoAssetsPanelState();
        builder.Services.AddScoped<ProviderConnectionMapRenderer>();

        // ── Authentication (XD01-52: MSAL.NET public-client flow, provider-agnostic seam per
        // XD01-48 — see EntraCiamMauiAuthenticationProvider) ────────────────────────────────
        builder.Services.AddAuthorizationCore();
        builder.Services.AddGeoAssetsMauiAuthentication(builder.Configuration);
        builder.Services.AddScoped<MauiMsalAuthenticationStateProvider>();
        builder.Services.AddScoped<AuthenticationStateProvider>(
            sp => sp.GetRequiredService<MauiMsalAuthenticationStateProvider>());
        builder.Services.AddScoped<ICurrentUserAccessor, MauiCurrentUserAccessor>();
        builder.Services.AddScoped<IAuthNavigationService, MauiAuthNavigationService>();

        // Attaches a silently-acquired bearer token to the "GeoAssetsServer" named HttpClient —
        // see MsalAuthorizationHandler's doc comment: no current caller requests this client by
        // name, registered ahead of MAUI gaining a REST call site against GeoAssets.Server.
        builder.Services.AddTransient<MsalAuthorizationHandler>();
        builder.Services.AddHttpClient("GeoAssetsServer")
            .AddHttpMessageHandler<MsalAuthorizationHandler>();

        builder.Services.AddSingleton<ActiveAssetProvider>();
        builder.Services.AddSingleton<IAssetProvider>(sp => new ObservableAssetProvider(
            new ValidatingAssetProvider(sp.GetRequiredService<ActiveAssetProvider>()),
            sp.GetRequiredService<ILogger<ObservableAssetProvider>>(),
            sp.GetRequiredService<TimeProvider>()));
        builder.Services.AddGeoAssetsPostgres();
        builder.Services.AddScoped<IStorageService, FileStorageService>();
        builder.Services.Configure<MapInteropOptions>(
            builder.Configuration.GetSection("MapInterop"));
        builder.Services.AddScoped<MapInteropService>();
        builder.Services.AddScoped<IMapInterop>(sp => new ObservableMapInterop(
            sp.GetRequiredService<MapInteropService>(),
            sp.GetRequiredService<ILogger<ObservableMapInterop>>(),
            sp.GetRequiredService<TimeProvider>()));
        builder.Services.AddScoped<AssetService>();
        builder.Services.AddScoped<IAssetService>(sp => new ObservableAssetService(
            sp.GetRequiredService<AssetService>(),
            sp.GetRequiredService<ILogger<ObservableAssetService>>(),
            sp.GetRequiredService<TimeProvider>()));

        return builder.Build();
    }
}
