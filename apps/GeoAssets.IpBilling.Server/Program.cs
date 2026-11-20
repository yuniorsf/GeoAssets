using GeoAssets.Identity.Authorization.Modules;
using GeoAssets.Infrastructure.Observability;
using GeoAssets.IpBilling.Calculation;
using GeoAssets.IpBilling.Persistence;
using GeoAssets.IpBilling.Resolution;
using GeoAssets.IpBilling.Server;
using GeoAssets.IpBilling.Workflow;
using GeoAssets.Provider.Rest;
using GeoAssets.Provider.Xlsx;
using GeoAssets.Workflow;
using GeoAssets.Workflow.Hosting;
using GeoAssets.Workflow.Rest;
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

// ── The kernel (GeoCoreServer:BaseUrl = its root, e.g. http://geoassets-server:8080) — reached only over REST. ──────
var kernelBaseUrl = builder.Configuration["GeoCoreServer:BaseUrl"]
    ?? throw new InvalidOperationException(
        "Configuration 'GeoCoreServer:BaseUrl' not found. " +
        "Set it to the kernel's root URL in appsettings.json or via the GeoCoreServer__BaseUrl environment variable.");
var kernelBaseAddress = new Uri(kernelBaseUrl.TrimEnd('/') + "/", UriKind.Absolute);

builder.Services.AddIpBillingPersistence(o => o.UseNpgsql(
    connectionString,
    npgsql => npgsql.MigrationsAssembly("GeoAssets.IpBilling.Server")));

builder.Services.AddOrderTypeRegistry(IpBillingOrderTypes.RegisterAll);
builder.Services.AddWorkflowPersistence(o => o.UseNpgsql(
    connectionString,
    npgsql => npgsql.MigrationsAssembly("GeoAssets.IpBilling.Server")));

builder.Services.AddServiceAuthTokenProvider(builder.Configuration);
builder.Services.AddGeoCoreRestClient(new Uri(kernelBaseAddress, "api/geoassets").ToString());

// Machine to machine as GeoAssets.ServiceCaller (XD01-299): the kernel's generic organization-grant API (XD01-385) and
// the bootstrap registration of this vertical's permissions (XD01-377). The organization is always named in the route or
// the body of these routes, never in a header.
builder.Services.AddKernelServiceCallerClients(kernelBaseAddress.ToString());

// The caller's own identity, as the kernel answers it for the caller's token: what the RequireAuthorization("…")
// permission policies of the município grant endpoints decide on (this host has no identity database).
builder.Services.AddKernelIdentity(kernelBaseAddress);
builder.Services.AddGeoPermissionPolicies();

// ── Município grant provisioning (XD01-177, moved here from the kernel by XD01-385) and this vertical's permissions,
// published to the kernel by the bootstrap registration: retried with backoff; /healthz stays 200 (Degraded) until it is
// done, /readyz answers 503 until then. ────────────────────────────────────────────────────────────────────────────
builder.Services.AddScoped<MunicipioProvisioningService>();
builder.Services.AddSingleton<IModulePermissionContributor, IpBillingPermissionContributor>();
builder.Services.AddVerticalRegistration(
    builder.Configuration, new VerticalRegistrationSource(IpBillingPermissionContributor.IpBillingVerticalId));
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
app.UseKernelFailureTranslation();
app.UseGeoAssetsAuthentication();
app.UseGeoAssetsObservability();
app.MapIpBillingApi();
app.MapMunicipioGrantsApi();
app.MapVerticalReadiness();

await app.RunAsync().ConfigureAwait(false);
