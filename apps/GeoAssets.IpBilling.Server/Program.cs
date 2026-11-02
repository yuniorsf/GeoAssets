using GeoAssets.Infrastructure.Observability;
using GeoAssets.IpBilling.Calculation;
using GeoAssets.IpBilling.Persistence;
using GeoAssets.IpBilling.Resolution;
using GeoAssets.IpBilling.Server;
using GeoAssets.IpBilling.Workflow;
using GeoAssets.Provider.Rest;
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

builder.Services.AddGeoCoreRestClient(coreServerBaseUrl);
builder.Services.AddScoped<IPoleResolver, HttpPoleResolver>();
builder.Services.AddScoped<IIpBillingCalculationService, IpBillingCalculationService>();

var app = builder.Build();

app.UseGeoAssetsAuthentication();
app.UseGeoAssetsObservability();
app.MapIpBillingApi();

await app.RunAsync().ConfigureAwait(false);
