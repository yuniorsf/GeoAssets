using Blazored.LocalStorage;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Providers;
using GeoAssets.Core.Services;
using GeoAssets.Provider.InMemory;
using GeoAssets.Provider.Rest;
using GeoAssets.Provider.WFS;
using GeoAssets.Provider.WMS;
using GeoAssets.Provider.Shapefile;
using GeoAssets.Shared.Interfaces;
using GeoAssets.Shared.Localization;
using GeoAssets.Shared.Services;
using GeoAssets.Shared.Services.Observability;
using GeoAssets.Web;
using GeoAssets.Web.Extensions;
using GeoAssets.Web.Services;
using GeoAssets.Web.Services.Identity;
using GeoAssets.Web.Services.Session;
using GeoAssets.Workflow;
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

// Asset provider — in-memory cache + REST API client, wrapped by the observable decorator.
// TODO: add a "loading" state to the UI while the provider initializes and remove the "Loading..." placeholder from the map.
// TODO: load by configuration and support multiple provider types (e.g. in-memory for dev, REST for prod).
builder.Services.AddGeoAssetsInMemory();
builder.Services.AddGeoAssetsRest();
builder.Services.AddGeoAssetsWfs();
builder.Services.AddGeoAssetsWms();
builder.Services.AddGeoAssetsShapefile();

// Plugin registry — collects all IProviderPlugin registrations for the UI.
builder.Services.AddSingleton<ProviderPluginRegistry>();

// Boot loader — orchestrates the first-run provider selection flow.
builder.Services.AddScoped<IBootLoader, BootLoaderService>();

// Proxy follows the active pool entry; wrapped by the attribute-schema-validating
// decorator (XD01-10), then the observable decorator.
builder.Services.AddSingleton<ActiveAssetProvider>();
builder.Services.AddSingleton<IAssetProvider>(sp => new ObservableAssetProvider(
    new ValidatingAssetProvider(sp.GetRequiredService<ActiveAssetProvider>()),
    sp.GetRequiredService<ILogger<ObservableAssetProvider>>()));

builder.Services.AddScoped<IStorageService, WebStorageService>();

builder.Services.Configure<MapInteropOptions>(
    builder.Configuration.GetSection("MapInterop"));
builder.Services.AddScoped<MapInteropService>();
builder.Services.AddScoped<IMapInterop>(sp => new ObservableMapInterop(
    sp.GetRequiredService<MapInteropService>(),
    sp.GetRequiredService<ILogger<ObservableMapInterop>>()));

builder.Services.AddScoped<AssetService>();
builder.Services.AddScoped<IAssetService>(sp => new ObservableAssetService(
    sp.GetRequiredService<AssetService>(),
    sp.GetRequiredService<ILogger<ObservableAssetService>>()));

// ── Service Order workflow — in-memory, session-scoped for this first pass.
// See XD01-8 for the durable/Postgres-backed alternative via GeoAssets.Server.
builder.Services.AddOrderTypeRegistry();
builder.Services.AddWorkflowInMemory();
builder.Services.AddServiceOrderRules();
builder.Services.AddScoped<WorkflowPrincipalFactory>();

// ── Build + seed + run ────────────────────────────────────────────────────────
var host = builder.Build();

var logger = host.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("GeoAssets starting — environment: {Environment}", builder.HostEnvironment.Environment);

host.Services.GetRequiredService<IdentitySeeder>().Seed();
host.Services.GetRequiredService<UserProvisioningService>();

await host.RunAsync();
