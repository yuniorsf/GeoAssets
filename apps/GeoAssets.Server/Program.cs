using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Providers;
using GeoAssets.Provider.PostgreSQL;
using GeoAssets.Server;
using GeoAssets.Workflow;
using GeoAssets.Workflow.Orders;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ── Connection string ─────────────────────────────────────────────────────────
// Read from appsettings.json "ConnectionStrings:GeoAssets"
// or override with env var: ConnectionStrings__GeoAssets
var connectionString = builder.Configuration.GetConnectionString("GeoAssets")
    ?? throw new InvalidOperationException(
        "Connection string 'GeoAssets' not found. " +
        "Set it in appsettings.json or via the ConnectionStrings__GeoAssets environment variable.");

// ── PostgreSQL provider ───────────────────────────────────────────────────────
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddGeoAssetsPostgres();
builder.Services.AddSingleton<IAssetProvider>(sp =>
    new ValidatingAssetProvider(
        sp.GetRequiredService<IPostgresProviderFactory>().Create(connectionString)));

// IDbContextFactory<GeoAssetsDbContext> — used by WmsPostGisRenderer so each
// tile request opens its own short-lived, thread-safe DbContext without touching
// the shared IAssetProvider in-memory cache.
builder.Services.AddGeoAssetsDbContextFactory(connectionString);

// WMS renderer — queries PostGIS directly (geometry + color projection only).
builder.Services.AddSingleton<WmsPostGisRenderer>();

// ── Service Order workflow persistence (XD01-8) — same Postgres database/connection
// as assets; workflow tables live alongside geo_entity/asset_type. OrderTypes seeded
// in-code by default, then overlaid with any DB-persisted types after the host builds
// (see LoadRegistryFromDbAsync below).
builder.Services.AddOrderTypeRegistry();
builder.Services.AddWorkflowPersistence(o => o.UseNpgsql(
    connectionString,
    // ServiceOrderDbContext lives in GeoAssets.Workflow.EFCore (kept provider-agnostic —
    // see its own doc comment), so migrations targeting Postgres are generated into this
    // (Server) project instead, which already legitimately depends on Npgsql.
    npgsql => npgsql.MigrationsAssembly("GeoAssets.Server")));

// ── CORS ——────────────────────────────────────────────────────────────────────
// Allow the Blazor WASM dev server origins configured in appsettings.json.
var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? [];

builder.Services.AddCors(opts =>
    opts.AddDefaultPolicy(p =>
        p.WithOrigins(allowedOrigins)
         .AllowAnyHeader()
         .AllowAnyMethod()));

// ── Build ─────────────────────────────────────────────────────────────────────
var app = builder.Build();

// Overlay any DB-persisted OrderTypes on top of the seeded defaults.
await app.Services.LoadRegistryFromDbAsync();

app.UseCors();

// Expose all GeoAssets REST endpoints under /api/geoassets
// Endpoints: GET/POST /features, GET/PUT/DELETE /features/{id},
//            POST /features/bulk, POST /features/load, DELETE /features,
//            GET/POST /asset-types, DELETE /asset-types/{id}
app.MapGeoAssetsApi();

// Service Order + Order Type REST endpoints under /api/workflow (XD01-8) —
// see ServiceOrdersRestApiExtensions for the full endpoint list.
app.MapServiceOrdersApi();

// Standalone OGC endpoints for external GIS clients (CORS not required
// for server-to-server or native desktop tools).
app.MapWfsApi();  // GET /wfs — OGC WFS 2.0
app.MapWmsApi();  // GET /wms — OGC WMS 1.1.1

app.Run();
