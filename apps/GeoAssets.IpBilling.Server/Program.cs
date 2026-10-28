using GeoAssets.Infrastructure.Observability;
using GeoAssets.IpBilling.Persistence;
using GeoAssets.IpBilling.Resolution;
using GeoAssets.IpBilling.Server;
using GeoAssets.Provider.Rest;
using GeoAssets.Workflow;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGeoAssetsObservability(builder.Configuration);
builder.Services.AddHealthChecks();
builder.Services.AddSingleton(TimeProvider.System);

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

builder.Services.AddOrderTypeRegistry();
builder.Services.AddWorkflowPersistence(o => o.UseNpgsql(
    connectionString,
    npgsql => npgsql.MigrationsAssembly("GeoAssets.IpBilling.Server")));

builder.Services.AddGeoCoreRestClient(coreServerBaseUrl);
builder.Services.AddScoped<IPoleResolver, HttpPoleResolver>();

var app = builder.Build();

app.UseGeoAssetsObservability();
app.MapIpBillingApi();

await app.RunAsync().ConfigureAwait(false);

