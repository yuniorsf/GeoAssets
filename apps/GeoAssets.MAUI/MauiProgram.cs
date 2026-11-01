using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Localization;
using GeoAssets.Core.Navigation;
using GeoAssets.Core.Services;
using GeoAssets.Core.Providers;
using GeoAssets.Identity.Authentication;
using GeoAssets.Identity.Authorization.Services;
using GeoAssets.MAUI.Extensions;
using GeoAssets.MAUI.Services.Identity;
using GeoAssets.MAUI.Services.Localization;
using GeoAssets.MAUI.Services.Evidence;
using GeoAssets.MAUI.Services.Offline;
using GeoAssets.Provider.PostgreSQL;
using GeoAssets.MAUI.Services;
using GeoAssets.Shared.Interfaces;
using GeoAssets.Shared.Navigation;
using GeoAssets.Shared.Services;
using GeoAssets.Shared.Services.Evidence;
using GeoAssets.Shared.Services.Observability;
using GeoAssets.Shared.Services.Offline;
using GeoAssets.Workflow;
using GeoAssets.Workflow.Rest;
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

        // AddMauiBlazorWebView() is annotated supported from Android API 23 — matching this
        // project's own AndroidMinSdkVersion (GeoAssets.MAUI.csproj) — while CA1416 reads the
        // net10.0-android TFM's default platform-version floor (API 21) unless the call is
        // flow-guarded by a version check the analyzer recognizes (a runtime-value disjunction
        // like "!IsAndroid() || IsAndroidVersionAtLeast(23)" is NOT recognized; it must be the
        // bare "if (IsAndroidVersionAtLeast(n))" shape). That shape would always evaluate false
        // on iOS/MacCatalyst/Windows at runtime, silently skipping registration there — so the
        // guard is scoped to the Android compile only via #if ANDROID (a MAUI SDK-defined
        // preprocessor symbol), leaving the other TFMs' call unguarded exactly as before. This
        // replaces a prior #pragma warning disable CA1416 with the documented fix instead of a
        // suppression; CA1416 did not fire on this call for any other TFM.
#if ANDROID
        if (OperatingSystem.IsAndroidVersionAtLeast(23))
        {
            builder.Services.AddMauiBlazorWebView();
        }
#else
        builder.Services.AddMauiBlazorWebView();
#endif

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        AddCrossCuttingServices(builder);
        AddAuthentication(builder);
        AddIdentityAuthorization(builder);
        AddServiceOrderWorkflow(builder);
        AddAssetsAndMap(builder);
        AddOfflineSync(builder);
        AddEvidenceCapture(builder);

        return builder.Build();
    }

    /// <summary>
    /// Observability (XD01-43): wrapped with the same Observable* decorators as
    /// apps/GeoAssets.Web/Program.cs, giving MAUI the same import/render/repository-timing
    /// telemetry Blazor Web already has. Deliberately NOT wired to AddGeoAssetsObservability
    /// (GeoAssets.Infrastructure.Observability) or an OTLP exporter here:
    ///   - That extension is ASP.NET Core-oriented (FrameworkReference on
    ///     Microsoft.AspNetCore.App, ASP.NET Core request instrumentation, a /healthz
    ///     endpoint) — none of it applies to a MAUI client, which hosts no HTTP server.
    ///   - A mobile/desktop app is a distributed binary an end user's device — unlike
    ///     GeoAssets.Server, a vendor OTLP endpoint/API-key credential embedded in it can be
    ///     extracted by decompiling the package. The safe pattern for client telemetry is
    ///     proxying through a trusted backend, not exporting directly from the client; that's
    ///     a separate, larger design (out of scope here).
    /// The decorators below are safe with no exporter at all — ImportDiagnostics'
    /// ActivitySource/Meter are zero-cost no-ops with nothing listening, exactly like
    /// Blazor Web before OTel is wired up client-side.
    /// </summary>
    private static void AddCrossCuttingServices(MauiAppBuilder builder)
    {
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<IProviderPool, ProviderPool>();

        // Cross-cutting panel state (XD01-82/83) — same self-sufficient-DI wiring as
        // apps/GeoAssets.Web/Program.cs (XD01-84).
        builder.Services.AddGeoAssetsPanelState();
        builder.Services.AddScoped<ProviderConnectionMapRenderer>();

        // Left-nav menu (XD01-79/85) — same wiring as apps/GeoAssets.Web/Program.cs. NavMenu
        // tolerates the absence of IGeoAuthorizationService (not registered here), so this alone
        // is enough for MAUI to render the same 6 items minus anything permission-gated.
        builder.Services.AddGeoAssetsNavigation(typeof(OverviewMenuItem).Assembly);
    }

    /// <summary>
    /// XD01-52: MSAL.NET public-client flow, provider-agnostic seam per XD01-48 — see
    /// EntraCiamMauiAuthenticationProvider.
    /// </summary>
    private static void AddAuthentication(MauiAppBuilder builder)
    {
        builder.Services.AddAuthorizationCore();
        builder.Services.AddGeoAssetsMauiAuthentication(builder.Configuration);
        builder.Services.AddScoped<MauiMsalAuthenticationStateProvider>();
        builder.Services.AddScoped<AuthenticationStateProvider>(
            sp => sp.GetRequiredService<MauiMsalAuthenticationStateProvider>());
        builder.Services.AddScoped<ICurrentUserAccessor, MauiCurrentUserAccessor>();
        builder.Services.AddScoped<IAuthNavigationService, MauiAuthNavigationService>();

        // Attaches a silently-acquired bearer token to the "GeoAssetsServer" named HttpClient —
        // see MsalAuthorizationHandler's doc comment. Identity authorization and the Service
        // Order workflow (both below) are this client's first real callers (XD01-24).
        builder.Services.AddTransient<MsalAuthorizationHandler>();
        builder.Services.AddHttpClient("GeoAssetsServer")
            .AddHttpMessageHandler<MsalAuthorizationHandler>();
    }

    /// <summary>
    /// Identity authorization — REST-backed against GeoAssets.Server, same shape as
    /// apps/GeoAssets.Web/Extensions/GeoIdentityRestExtensions.AddGeoIdentityRest, but only
    /// the one piece WorkflowPrincipalFactory actually needs — see
    /// RestGeoAuthorizationService's doc comment for why this is a small duplicated copy
    /// instead of a shared registration (XD01-24). Also registers the localization stand-in
    /// (NoOpJsonStringLocalizer) alongside it.
    /// </summary>
    private static void AddIdentityAuthorization(MauiAppBuilder builder)
    {
        var geoAssetsServerBaseUrl = builder.Configuration["GeoAssetsServer:BaseUrl"]
            ?? throw new InvalidOperationException("GeoAssetsServer:BaseUrl is not configured.");
        builder.Services.AddScoped<IGeoAuthorizationService>(sp =>
        {
            var client = sp.GetRequiredService<IHttpClientFactory>().CreateClient("GeoAssetsServer");
            client.BaseAddress = new Uri(geoAssetsServerBaseUrl.TrimEnd('/') + "/api/identity/");
            return new RestGeoAuthorizationService(client);
        });

        // Localization stand-in (XD01-24) — see NoOpJsonStringLocalizer's doc comment: MAUI has
        // no real translation loader yet, so LocalizedComponentBase-derived components (Service
        // Orders among them) get raw i18n keys instead of throwing on a missing registration.
        builder.Services.AddSingleton<IJsonStringLocalizer, NoOpJsonStringLocalizer>();
    }

    /// <summary>
    /// Service Order workflow — REST-backed via GeoAssets.Server, same pattern as
    /// apps/GeoAssets.Web/Program.cs (XD01-24, XD01-8 originally for Web).
    /// </summary>
    private static void AddServiceOrderWorkflow(MauiAppBuilder builder)
    {
        builder.Services.AddOrderTypeRegistry();

        var serviceOrdersApiBaseUrl = builder.Configuration["ServiceOrders:ApiBaseUrl"]
            ?? throw new InvalidOperationException("ServiceOrders:ApiBaseUrl is not configured.");
        builder.Services.AddWorkflowRest(serviceOrdersApiBaseUrl);

        builder.Services.AddServiceOrderRules();
        builder.Services.AddScoped<WorkflowPrincipalFactory>();
    }

    private static void AddAssetsAndMap(MauiAppBuilder builder)
    {
        builder.Services.AddSingleton<ActiveAssetProvider>();
        builder.Services.AddSingleton<IAssetProvider>(sp => new ObservableAssetProvider(
            new ValidatingAssetProvider(sp.GetRequiredService<ActiveAssetProvider>()),
            sp.GetRequiredService<ILogger<ObservableAssetProvider>>(),
            sp.GetRequiredService<TimeProvider>()));
        builder.Services.AddSingleton<FeatureRenderPipeline>();
        builder.Services.AddGeoAssetsPostgres();
        builder.Services.AddScoped<IStorageService, FileStorageService>();
        builder.Services.AddScoped<INativeFilePicker, MauiNativeFilePicker>();
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
    }

    /// <summary>
    /// Offline-first scaffolding for contractor field crews (XD01-243): SQLCipher-encrypted
    /// local stores (<see cref="SqliteLocalServiceOrderStore"/> and friends) registered here
    /// since they're MAUI-only/platform-specific, then layered under <see cref="MauiSyncGateway"/>
    /// via <see cref="OfflineSyncServiceExtensions.AddFieldServiceOfflineSync"/> — the same
    /// REST-vs-local DI split <see cref="AddServiceOrderWorkflow"/> already uses.
    /// </summary>
    private static void AddOfflineSync(MauiAppBuilder builder)
    {
        builder.Services.AddSingleton<ILocalServiceOrderStore, SqliteLocalServiceOrderStore>();
        builder.Services.AddSingleton<ILocalActionOutbox, SqliteLocalActionOutbox>();
        builder.Services.AddSingleton<ILocalSyncCursorStore, SqliteLocalSyncCursorStore>();
        builder.Services.AddSingleton<ILocalMediaStagingStore, SqliteLocalMediaStagingStore>();
        builder.Services.AddSingleton<ILocalVectorTileCache, SqliteLocalVectorTileCache>();
        // XD01-276: offline cache backing NearestAvailableCtoAssigner (XD01-273) with zero connectivity.
        builder.Services.AddSingleton<ILocalCtoPortCacheStore, SqliteLocalCtoPortCacheStore>();

        var fieldServiceServerBaseUrl = builder.Configuration["FieldServiceServer:BaseUrl"]
            ?? throw new InvalidOperationException("FieldServiceServer:BaseUrl is not configured.");
        builder.Services.AddFieldServiceOfflineSync(fieldServiceServerBaseUrl.TrimEnd('/') + "/api/field-service/sync");

        // XD01-276: connectivity-aware outbox flush on reconnect, started once from App's constructor.
        builder.Services.AddSingleton<ConnectivitySyncTrigger>();
    }

    /// <summary>
    /// Field-work photo evidence capture (XD01-244): the portable gate/sign/verify pipeline
    /// (<see cref="EvidenceCaptureService"/>, <c>GeoAssets.Shared</c>) layered under the
    /// SkiaSharp watermark composer and GPS/compass/camera orchestration, both MAUI-only.
    ///
    /// Deliberately not wired here: a per-tenant HMAC secret. <see cref="MauiEvidenceCaptureGateway.CaptureAsync"/>
    /// takes it as a caller-supplied parameter rather than reading a config key, because no real
    /// secret-provisioning mechanism exists yet (it must be issued per tenant by a trusted
    /// server, not committed to source or generated locally like the offline DB's encryption key
    /// in <see cref="LocalOfflineDatabase"/>) — and no caller exists yet either: see
    /// XD01-295, which also covers the UI/API gap this gateway has no host to be invoked from.
    /// </summary>
    private static void AddEvidenceCapture(MauiAppBuilder builder)
    {
        builder.Services.AddFieldWorkEvidenceCapture();
        builder.Services.AddSingleton<IEvidenceWatermarkComposer, SkiaEvidenceWatermarkComposer>();
        builder.Services.AddScoped<MauiEvidenceCaptureGateway>();
    }
}
