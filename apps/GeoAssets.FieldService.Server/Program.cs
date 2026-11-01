using GeoAssets.Core.Interfaces;
using GeoAssets.FieldService.Server;
using GeoAssets.Identity.Authentication;
using GeoAssets.Infrastructure.Observability;
using GeoAssets.Provider.PostgreSQL.Data;
using GeoAssets.Provider.PostgreSQL.Interceptors;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ── Observability (traces + metrics + logs → OTLP) — also wires the /healthz liveness
// endpoint used below via UseGeoAssetsObservability (XD01-45's pattern). ─────────────────────
builder.Services.AddGeoAssetsObservability(builder.Configuration);
builder.Services.AddHealthChecks();

// ── Authentication (bearer-token validation against the CIAM tenant; see "AzureAdCiam"
// section in appsettings.json) — every endpoint requires an authenticated caller by default,
// identical to GeoAssets.Server (XD01-12). No business endpoints exist yet to actually exercise
// this (XD01-206's own AC), but the fallback policy already applies to anything mapped later. ──
builder.Services.AddGeoAssetsAuthentication(builder.Configuration);

// ── Tenant resolution (XD01-200/206) — same ICurrentUserAccessor/HttpTenantContext pattern as
// GeoAssets.Server, wired from day one so every later slice (contractor US ledger, mobile sync,
// archetype domain assemblies) builds on real per-request tenant resolution, not
// NullTenantContext. ───────────────────────────────────────────────────────────────────────
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserAccessor>(sp =>
    new ClaimsPrincipalCurrentUserAccessor(
        () => sp.GetRequiredService<IHttpContextAccessor>().HttpContext?.User));
builder.Services.AddScoped<ITenantContext, HttpTenantContext>();

// ── PostgreSQL (XD01-199/XD01-203) — GeoAssetsDbContext registered with both tenant
// interceptors attached via the (IServiceProvider, DbContextOptionsBuilder) overload, so each
// request's own scoped ITenantContext drives TenantSessionInterceptor/TenantTransactionInterceptor
// rather than a single process-wide value. Scoped (not the IDbContextFactory/warmed-singleton
// pattern GeoAssets.Server uses for its IAssetProvider cache) since this host has no business
// reads yet to warm a cache for. ──────────────────────────────────────────────────────────────
var connectionString = builder.Configuration.GetConnectionString("GeoAssets")
    ?? throw new InvalidOperationException(
        "Connection string 'GeoAssets' not found. " +
        "Set it in appsettings.json or via the ConnectionStrings__GeoAssets environment variable.");

builder.Services.AddScoped<TenantSessionInterceptor>();
builder.Services.AddScoped<TenantTransactionInterceptor>();
builder.Services.AddDbContext<GeoAssetsDbContext>((sp, options) => options
    .UseNpgsql(connectionString, npgsql => npgsql.UseNetTopologySuite())
    .AddInterceptors(
        sp.GetRequiredService<TenantSessionInterceptor>(),
        sp.GetRequiredService<TenantTransactionInterceptor>()));

var app = builder.Build();

app.UseGeoAssetsAuthentication();
app.UseGeoAssetsObservability();

// No business endpoints yet — see XD01-206's own AC. Later tickets (XD01-207 contractor US
// ledger, XD01-208 mobile offline sync, the Electrical/Sanitation/NeutralPole domain assemblies)
// map their own endpoints here.

await app.RunAsync().ConfigureAwait(false);
