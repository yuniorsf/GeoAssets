using Blazored.LocalStorage;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Services;
using GeoAssets.Provider.Active;
using GeoAssets.Provider.InMemory;
using GeoAssets.Provider.Observable;
using GeoAssets.Provider.Rest;
using GeoAssets.Shared.Interfaces;
using GeoAssets.Shared.Localization;
using GeoAssets.Shared.Services;
using GeoAssets.Shared.Services.Observability;
using GeoAssets.Web;
using GeoAssets.Web.Extensions;
using GeoAssets.Web.Services;
using GeoAssets.Web.Services.Identity;
using GeoAssets.Web.Services.Session;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// ── Authentication — Azure AD via MSAL ───────────────────────────────────────
builder.Services.AddMsalAuthentication(options =>
{
    builder.Configuration.Bind("AzureAd", options.ProviderOptions.Authentication);
    // Use redirect instead of popup to avoid COOP (Cross-Origin-Opener-Policy)
    // browser restrictions that block window.closed monitoring in popup flow.
    options.ProviderOptions.LoginMode = "redirect";
});

// ── Authorization ─────────────────────────────────────────────────────────────
builder.Services.AddAuthorizationCore();

// ── GeoAssets Identity (in-memory repos + authorization service) ─────────────
builder.Services.AddGeoIdentityWasm();

// ── Session timeout (inactivity = configurable via appsettings.json → Session) ─
builder.Services.Configure<SessionConfig>(opts =>
    builder.Configuration.GetSection("Session").Bind(opts));
builder.Services.AddScoped<SessionTimeoutService>();

// ── Auth navigation (MSAL logout/login wrappers) ─────────────────────────────G
builder.Services.AddScoped<IAuthNavigationService, MsalAuthNavigationService>();

// ── Infrastructure ────────────────────────────────────────────────────────────
builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddBlazoredLocalStorage();

// ── Localization ──────────────────────────────────────────────────────────────
builder.Services.AddGeoAssetsLocalization(opts =>
{
    opts.DefaultCulture    = "es";
    opts.SupportedCultures = ["es", "en", "pt"];
});
builder.Services.AddScoped<AppInsightsService>();
builder.Services.AddScoped<IAnalyticsService>(sp => sp.GetRequiredService<AppInsightsService>());

// ── GeoAssets core services ───────────────────────────────────────────────────

// Provider pool — each entry owns an independent InMemoryAssetProvider.
// The active entry is the editable workspace for all UI components.
builder.Services.AddSingleton<IProviderPool, InMemoryProviderPool>();

// REST provider — exposes a remote GeoAssets API as a pool entry.
// Connect via the 🔌 button in the ProviderPoolPanel using the API base URL.
builder.Services.AddGeoAssetsRest();

// Proxy follows the active pool entry; wrapped by the observable decorator.
builder.Services.AddSingleton<ActiveAssetProvider>();
builder.Services.AddSingleton<IAssetProvider>(sp => new ObservableAssetProvider(
    sp.GetRequiredService<ActiveAssetProvider>(),
    sp.GetRequiredService<ILogger<ObservableAssetProvider>>()));

builder.Services.AddScoped<IStorageService, WebStorageService>();

builder.Services.AddScoped<MapInteropService>();
builder.Services.AddScoped<IMapInterop>(sp => new ObservableMapInterop(
    sp.GetRequiredService<MapInteropService>(),
    sp.GetRequiredService<ILogger<ObservableMapInterop>>()));

builder.Services.AddScoped<AssetService>();
builder.Services.AddScoped<IAssetService>(sp => new ObservableAssetService(
    sp.GetRequiredService<AssetService>(),
    sp.GetRequiredService<ILogger<ObservableAssetService>>()));

// ── Build + seed + run ────────────────────────────────────────────────────────
var host = builder.Build();

var logger = host.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("GeoAssets starting — environment: {Environment}", builder.HostEnvironment.Environment);

host.Services.GetRequiredService<IdentitySeeder>().Seed();
host.Services.GetRequiredService<UserProvisioningService>();

await host.RunAsync();
