using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Providers;
using GeoAssets.Identity;
using GeoAssets.Identity.Authentication;
using GeoAssets.Infrastructure.Observability;
using GeoAssets.Provider.PostgreSQL;
using GeoAssets.Server;
using GeoAssets.Workflow;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ── Observability (traces + metrics + logs → OTLP, e.g. New Relic; see
// "Observability" section in appsettings.json) ─────────────────────────────────
builder.Services.AddGeoAssetsObservability(builder.Configuration);

// ── Authentication (bearer-token validation against the CIAM tenant; see
// "AzureAdCiam" section in appsettings.json) — every endpoint requires an
// authenticated caller by default (XD01-12) ─────────────────────────────────────
builder.Services.AddGeoAssetsAuthentication(builder.Configuration);

// UseGeoAssetsObservability() (below) wires a /healthz endpoint via UseHealthChecks,
// which requires HealthCheckService to be registered — without this call the host
// throws InvalidOperationException at startup (found via XD01-45's test coverage).
builder.Services.AddHealthChecks();

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

// ServiceOrderRules (XD01-16) — same singleton, role-grant configuration as
// apps/GeoAssets.Web/Program.cs, so server-side enforcement and the client UI agree on
// who can do what. ServerWorkflowPrincipalFactory builds the WorkflowPrincipal it evaluates
// against from the authenticated caller (IGeoAuthorizationService, registered below).
builder.Services.AddServiceOrderRules();
builder.Services.AddScoped<ServerWorkflowPrincipalFactory>();

// ── Identity (RBAC/ABAC) — EF Core backend against Postgres (XD01-14). Well-known
// roles/permissions/policies are seeded idempotently on every startup (see
// SeedGeoIdentityAsync below).
builder.Services.AddGeoIdentity(o => o.UseNpgsql(
    connectionString,
    npgsql => npgsql.MigrationsAssembly("GeoAssets.Server")));

// ICurrentUserAccessor reads the authenticated ClaimsPrincipal populated by
// AddGeoAssetsAuthentication (XD01-12) — pattern documented on
// GeoIdentityEFCoreServiceExtensions.AddGeoIdentity's own XML doc comment.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserAccessor>(sp =>
    new ClaimsPrincipalCurrentUserAccessor(
        () => sp.GetRequiredService<IHttpContextAccessor>().HttpContext?.User));

// AuthZ bridge (XD01-13) — lets endpoints declare .RequireAuthorization("SomePolicyName")
// (matching an AppPolicy.Name row) and get evaluated against IGeoAuthorizationService,
// the same policy engine the Blazor client already uses.
builder.Services.AddGeoAuthorizationPolicyBridge();

// Role assignment provider (XD01-59 Phase 2) — no-op until XD01-62 lands a real,
// Graph-backed provider to pass here.
builder.Services.AddRoleAssignmentProvider(builder.Configuration);

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

// Seed canonical identity roles/permissions/policies (idempotent, XD01-14).
await app.Services.SeedGeoIdentityAsync();

app.UseCors();

app.UseGeoAssetsAuthentication();

app.UseGeoAssetsObservability();

// Expose all GeoAssets REST endpoints under /api/geoassets
// Endpoints: GET/POST /features, GET/PUT/DELETE /features/{id},
//            POST /features/bulk, POST /features/load, DELETE /features,
//            GET/POST /asset-types, DELETE /asset-types/{id}
app.MapGeoAssetsApi();

// Service Order + Order Type REST endpoints under /api/workflow (XD01-8) —
// see ServiceOrdersRestApiExtensions for the full endpoint list.
app.MapServiceOrdersApi();

// Read-only identity/authorization endpoints under /api/identity (XD01-18) —
// see IdentityRestApiExtensions for the full endpoint list.
app.MapIdentityApi();

// Standalone OGC endpoints for external GIS clients (CORS not required
// for server-to-server or native desktop tools).
app.MapWfsApi();  // GET /wfs — OGC WFS 2.0
app.MapWmsApi();  // GET /wms — OGC WMS 1.1.1

app.Run();
