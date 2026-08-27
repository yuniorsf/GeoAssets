using Blazored.LocalStorage;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Navigation;
using GeoAssets.Core.Providers;
using GeoAssets.Core.Services;
using GeoAssets.Identity.Authentication;
using GeoAssets.Provider.InMemory;
using GeoAssets.Provider.Rest;
using GeoAssets.Provider.WFS;
using GeoAssets.Provider.WMS;
using GeoAssets.Provider.Shapefile;
using GeoAssets.Shared.Interfaces;
using GeoAssets.Shared.Localization;
using GeoAssets.Shared.Navigation;
using GeoAssets.Shared.Services;
using GeoAssets.Shared.Services.Observability;
using GeoAssets.Shared.Theming;
using GeoAssets.Web;
using GeoAssets.Web.Extensions;
using GeoAssets.Web.Services;
using GeoAssets.Web.Services.Identity;
using GeoAssets.Web.Services.Session;
using GeoAssets.Workflow;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rest;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.JSInterop;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Per-developer, git-ignored overrides (e.g. real AzureAdCiam tenant/client IDs, kept out of
// the public repo — see wwwroot/appsettings.Local.json.example). Silently absent for anyone
// who hasn't created the file; loaded last so it wins over appsettings.{Environment}.json.
if (builder.HostEnvironment.IsDevelopment())
{
    using var localSettingsHttp = new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) };
    try
    {
        // GetStreamAsync returns a stream backed by the browser's fetch API, which only
        // supports async reads — AddJsonStream parses synchronously (JsonDocument.Parse),
        // which throws "net_http_synchronous_reads_not_supported" the moment the file is
        // actually present. Materialize the content as a string first (fully async), then
        // hand AddJsonStream a plain in-memory stream, which supports synchronous reads.
        var localSettingsJson = await localSettingsHttp.GetStringAsync("appsettings.Local.json");
        builder.Configuration.AddJsonStream(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(localSettingsJson)));
    }
    catch (HttpRequestException)
    {
        // appsettings.Local.json not present — fine, it's optional.
    }
}

// ── Authentication (XD01-48: provider-agnostic seam; defaults to Entra External ID (CIAM)
// tenant via MSAL — see EntraCiamWasmAuthenticationProvider) ─────────────────────────────────
builder.Services.AddGeoAssetsWasmAuthentication(builder.Configuration);

// ── Authorization ─────────────────────────────────────────────────────────────
builder.Services.AddAuthorizationCore();

// ICurrentUserAccessor reads the current user from Blazor's MSAL auth state — needed by
// both identity backends below (InMemory dev seeding and Rest), so registered once here.
builder.Services.AddScoped<ICurrentUserAccessor, BlazorWasmCurrentUserAccessor>();

// ── Session timeout (inactivity = configurable via appsettings.json → Session) ─
builder.Services.Configure<SessionConfig>(opts =>
    builder.Configuration.GetSection("Session").Bind(opts));
builder.Services.AddScoped<SessionTimeoutService>();

// ── Auth navigation (Blazor remote-auth login/logout wrappers — vendor-agnostic per
// BlazorRemoteAuthNavigationService's doc comment, XD01-50) ──────────────────────────────────
builder.Services.AddScoped<IAuthNavigationService, BlazorRemoteAuthNavigationService>();

// ── Infrastructure ────────────────────────────────────────────────────────────
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddBlazoredLocalStorage();

// ── Localization ──────────────────────────────────────────────────────────────
builder.Services.AddGeoAssetsLocalization(opts =>
{
    opts.DefaultCulture    = "es";
    opts.SupportedCultures = ["es", "en", "pt"];
});

// ── Theming (dark/light/system, XD01-76) ────────────────────────────────────────
builder.Services.AddGeoAssetsTheming();
builder.Services.AddScoped<AppInsightsService>();
builder.Services.AddScoped<IAnalyticsService>(sp => sp.GetRequiredService<AppInsightsService>());

// ── GeoAssets core services ───────────────────────────────────────────────────

// Asset provider — in-memory cache + REST API client, wrapped by the observable decorator.
// TODO: add a "loading" state to the UI while the provider initializes and remove the "Loading..." placeholder from the map.
// TODO: load by configuration and support multiple provider types (e.g. in-memory for dev, REST for prod).
builder.Services.AddGeoAssetsInMemory();

// AuthorizationMessageHandler (MSAL) attaches the CIAM access token to the "GeoAssetsServer"
// named HttpClient that RestProviderFactory requests (XD01-17) — scoped to the configured,
// trusted GeoAssets.Server origin only, never to arbitrary REST URLs a user might type into
// the provider-pool UI (RestProviderPlugin).
builder.Services.AddHttpClient("GeoAssetsServer")
    .AddHttpMessageHandler(sp =>
    {
        var handler = sp.GetRequiredService<AuthorizationMessageHandler>();
        handler.ConfigureHandler(
            authorizedUrls: [builder.Configuration["GeoAssetsServer:BaseUrl"] ?? ""],
            scopes:         [builder.Configuration["GeoAssetsServer:ApiScope"] ?? ""]);
        return handler;
    });

// ── GeoAssets Identity — in-memory dev/demo seed (default) or HTTP-backed against
// GeoAssets.Server (XD01-18), switched by "Identity:Backend" config ("InMemory" | "Rest";
// env var override: Identity__Backend). In-memory stays the default so the app still runs
// with zero config against a Postgres server, mirroring "ServiceOrders:Backend" below.
var identityBackend = builder.Configuration["Identity:Backend"] ?? "InMemory";
var identityUseRest = string.Equals(identityBackend, "Rest", StringComparison.OrdinalIgnoreCase);

if (identityUseRest)
    builder.Services.AddGeoIdentityRest();
else
    builder.Services.AddGeoIdentityWasmDev();

// Pending-invitation redirect check (XD01-89) — backend-agnostic, so registered here rather
// than inside either AddGeoIdentityRest or AddGeoIdentityWasmDev: its dependencies
// (ICurrentUserAccessor, IPendingInvitationRepository, NavigationManager) are already
// registered by both branches above.
builder.Services.AddScoped<InvitationRedirectGate>();

builder.Services.AddGeoAssetsRest();
builder.Services.AddGeoAssetsWfs();
builder.Services.AddGeoAssetsWms();
builder.Services.AddGeoAssetsShapefile();

// Plugin registry — collects all IProviderPlugin registrations for the UI.
builder.Services.AddSingleton<ProviderPluginRegistry>();

// Boot loader — orchestrates the first-run provider selection flow.
builder.Services.AddScoped<IBootLoader, BootLoaderService>();

// Cross-cutting panel state (XD01-82/83) — lets LayerManager/ProviderPoolPanel/AssetList/
// Index self-inject their map-context and selection state instead of receiving them as
// parameters/EventCallbacks from whatever page hosts them (XD01-84). ProviderConnectionMapRenderer
// is force-resolved once in Index.razor's OnInitializedAsync to start its IProviderPool.EntryAdded
// subscription — merely registering it here isn't enough to make it run.
builder.Services.AddGeoAssetsPanelState();
builder.Services.AddScoped<ProviderConnectionMapRenderer>();

// Left-nav menu (XD01-79/85) — discovers every concrete MenuItemBase in the assembly holding
// OverviewMenuItem/LayersMenuItem/etc. and assembles them into the MenuRegistry NavMenu.razor
// builds its tree from.
builder.Services.AddGeoAssetsNavigation(typeof(OverviewMenuItem).Assembly);

// Proxy follows the active pool entry; wrapped by the attribute-schema-validating
// decorator (XD01-10), then the observable decorator.
builder.Services.AddSingleton<ActiveAssetProvider>();
builder.Services.AddSingleton<IAssetProvider>(sp => new ObservableAssetProvider(
    new ValidatingAssetProvider(sp.GetRequiredService<ActiveAssetProvider>()),
    sp.GetRequiredService<ILogger<ObservableAssetProvider>>(),
    sp.GetRequiredService<TimeProvider>()));

builder.Services.AddScoped<IStorageService, WebStorageService>();

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

// ── Service Order workflow — in-memory (default) or REST-backed via GeoAssets.Server,
// switched by "ServiceOrders:Backend" config ("InMemory" | "Rest"; env var override:
// ServiceOrders__Backend). In-memory stays the default so the app still runs with zero
// config against a Postgres server (XD01-8). When "Rest", OrderTypes are also loaded from
// the server below, overlaying the seeded defaults — see AddWorkflowRest's doc comment.
builder.Services.AddOrderTypeRegistry();

var serviceOrdersBackend = builder.Configuration["ServiceOrders:Backend"] ?? "InMemory";
var serviceOrdersUseRest = string.Equals(serviceOrdersBackend, "Rest", StringComparison.OrdinalIgnoreCase);

if (serviceOrdersUseRest)
{
    var serviceOrdersApiBaseUrl = builder.Configuration["ServiceOrders:ApiBaseUrl"]
        ?? throw new InvalidOperationException(
            "ServiceOrders:Backend is 'Rest' but ServiceOrders:ApiBaseUrl is not configured.");
    builder.Services.AddWorkflowRest(serviceOrdersApiBaseUrl);
}
else
{
    builder.Services.AddWorkflowInMemory();
}

builder.Services.AddServiceOrderRules();
builder.Services.AddScoped<WorkflowPrincipalFactory>();

// ── Build + seed + run ────────────────────────────────────────────────────────
var host = builder.Build();

var logger = host.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("GeoAssets starting — environment: {Environment}", builder.HostEnvironment.Environment);

if (!identityUseRest)
{
    host.Services.GetRequiredService<IdentitySeeder>().Seed();
    host.Services.GetRequiredService<UserProvisioningService>();
}

if (serviceOrdersUseRest)
{
    var orderTypeRegistry = host.Services.GetRequiredService<OrderTypeRegistry>();
    var orderTypeRepo     = host.Services.GetRequiredService<IOrderTypeRepository>();
    await orderTypeRegistry.LoadFromAsync(orderTypeRepo);
}

// Application Insights (XD01-53) — initialized from configuration instead of the connection
// string being hardcoded in wwwroot/index.html, which (unlike appsettings.json) is served
// verbatim with zero fetch/parse step, so a hardcoded value there is committed and live the
// moment anyone opens the page source. No-op (appInsightsInterop.init checks for a falsy arg)
// until AzureAdCiam's own gitignored appsettings.Local.json override pattern supplies a real one.
var appInsightsConnectionString = builder.Configuration["Observability:AzureMonitor:ConnectionString"];
if (!string.IsNullOrWhiteSpace(appInsightsConnectionString))
    await host.Services.GetRequiredService<IJSRuntime>()
        .InvokeVoidAsync("appInsightsInterop.init", appInsightsConnectionString);

await host.RunAsync();
