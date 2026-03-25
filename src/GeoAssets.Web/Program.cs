using Blazored.LocalStorage;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Services;
using GeoAssets.Shared.Interfaces;
using GeoAssets.Shared.Services;
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

// ── Auth navigation (MSAL logout/login wrappers) ─────────────────────────────
builder.Services.AddScoped<IAuthNavigationService, MsalAuthNavigationService>();

// ── Infrastructure ────────────────────────────────────────────────────────────
builder.Services.AddBlazoredLocalStorage();

// ── GeoAssets core services ───────────────────────────────────────────────────
builder.Services.AddSingleton<IAssetRepository, InMemoryAssetRepository>();
builder.Services.AddScoped<IStorageService, WebStorageService>();
builder.Services.AddScoped<IMapInterop, MapInteropService>();
builder.Services.AddScoped<AssetService>();

// ── Build + seed + run ────────────────────────────────────────────────────────
var host = builder.Build();

host.Services.GetRequiredService<IdentitySeeder>().Seed();
host.Services.GetRequiredService<UserProvisioningService>();

await host.RunAsync();
