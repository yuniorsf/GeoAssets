using GeoAssets.Core.Interfaces;
using GeoAssets.Provider.PostgreSQL;
using GeoAssets.Server;

var builder = WebApplication.CreateBuilder(args);

// ── Connection string ─────────────────────────────────────────────────────────
// Read from appsettings.json "ConnectionStrings:GeoAssets"
// or override with env var: ConnectionStrings__GeoAssets
var connectionString = builder.Configuration.GetConnectionString("GeoAssets")
    ?? throw new InvalidOperationException(
        "Connection string 'GeoAssets' not found. " +
        "Set it in appsettings.json or via the ConnectionStrings__GeoAssets environment variable.");

// ── PostgreSQL provider ───────────────────────────────────────────────────────
builder.Services.AddGeoAssetsPostgres();
builder.Services.AddSingleton<IAssetProvider>(sp =>
    sp.GetRequiredService<IPostgresProviderFactory>().Create(connectionString));

// IDbContextFactory<GeoAssetsDbContext> — used by WmsPostGisRenderer so each
// tile request opens its own short-lived, thread-safe DbContext without touching
// the shared IAssetProvider in-memory cache.
builder.Services.AddGeoAssetsDbContextFactory(connectionString);

// WMS renderer — queries PostGIS directly (geometry + color projection only).
builder.Services.AddSingleton<WmsPostGisRenderer>();

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

app.UseCors();

// Expose all GeoAssets REST endpoints under /api/geoassets
// Endpoints: GET/POST /features, GET/PUT/DELETE /features/{id},
//            POST /features/bulk, POST /features/load, DELETE /features,
//            GET/POST /asset-types, DELETE /asset-types/{id}
app.MapGeoAssetsApi();

// Standalone OGC endpoints for external GIS clients (CORS not required
// for server-to-server or native desktop tools).
app.MapWfsApi();  // GET /wfs — OGC WFS 2.0
app.MapWmsApi();  // GET /wms — OGC WMS 1.1.1

app.Run();
