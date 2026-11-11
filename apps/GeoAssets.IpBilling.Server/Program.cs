using GeoAssets.Infrastructure.Observability;
using GeoAssets.IpBilling.Calculation;
using GeoAssets.IpBilling.Persistence;
using GeoAssets.IpBilling.Resolution;
using GeoAssets.IpBilling.Server;
using GeoAssets.IpBilling.Workflow;
using GeoAssets.Provider.Rest;
using GeoAssets.Provider.Xlsx;
using GeoAssets.Workflow;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGeoAssetsObservability(builder.Configuration);
builder.Services.AddHealthChecks();
builder.Services.AddSingleton(TimeProvider.System);

// ── Authentication (bearer-token validation against the CIAM tenant; see
// "AzureAdCiam" section in appsettings.json) — every endpoint requires an
// authenticated caller by default (XD01-177, closing a previously-flagged gap). ────
builder.Services.AddGeoAssetsAuthentication(builder.Configuration);

var connectionString = builder.Configuration.GetConnectionString("IpBilling")
    ?? builder.Configuration.GetConnectionString("GeoAssets")
    ?? throw new InvalidOperationException(
        "Connection string 'IpBilling' (or fallback 'GeoAssets') not found.");

var coreServerBaseUrl = builder.Configuration["GeoCoreServer:BaseUrl"]
    ?? throw new InvalidOperationException(
        "Configuration 'GeoCoreServer:BaseUrl' not found.");

builder.Services.AddIpBillingPersistence(o => o.UseNpgsql(
    connectionString,
    npgsql => npgsql.MigrationsAssembly("GeoAssets.IpBilling.Server")));

builder.Services.AddOrderTypeRegistry(IpBillingOrderTypes.RegisterAll);
builder.Services.AddWorkflowPersistence(o => o.UseNpgsql(
    connectionString,
    npgsql => npgsql.MigrationsAssembly("GeoAssets.IpBilling.Server")));

builder.Services.AddServiceAuthTokenProvider(builder.Configuration);
builder.Services.AddGeoCoreRestClient(coreServerBaseUrl);
builder.Services.AddScoped<IPoleResolver, HttpPoleResolver>();
builder.Services.AddScoped<IIpBillingCalculationService, IpBillingCalculationService>();
builder.Services.AddGeoAssetsXlsx();

// ── CORS (XD01-178) — allow the Blazor WASM dev server origins configured in
// appsettings.json, same pattern as GeoAssets.Server. Needed because the admin
// Municípios/Lamp Reference pages in GeoAssets.Web call this host directly (a
// separate origin from GeoAssets.Server). ──────────────────────────────────────
var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? [];

builder.Services.AddCors(opts =>
    opts.AddDefaultPolicy(p =>
        p.WithOrigins(allowedOrigins)
         .AllowAnyHeader()
         .AllowAnyMethod()));

var app = builder.Build();

app.UseCors();
app.UseGeoAssetsAuthentication();
app.UseGeoAssetsObservability();
app.MapIpBillingApi();

await app.RunAsync().ConfigureAwait(false);
