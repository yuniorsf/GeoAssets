using GeoAssets.Core.Interfaces;
using GeoAssets.FieldService.Server;
using GeoAssets.Identity.Authentication;
using GeoAssets.Infrastructure.Observability;
using GeoAssets.Provider.PostgreSQL.Data;
using GeoAssets.Provider.PostgreSQL.Interceptors;
using GeoAssets.Workflow.Contractors;
using GeoAssets.Workflow.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ── Observability (traces + metrics + logs → OTLP) — also wires the /healthz liveness
// endpoint used below via UseGeoAssetsObservability (XD01-45's pattern). ─────────────────────
builder.Services.AddGeoAssetsObservability(builder.Configuration);
builder.Services.AddHealthChecks();

// ── Authentication (bearer-token validation against the CIAM tenant; see "AzureAdCiam"
// section in appsettings.json) — every endpoint requires an authenticated caller by default,
// identical to GeoAssets.Server (XD01-12). ───────────────────────────────────────────────────
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
// pattern GeoAssets.Server uses for its IAssetProvider cache) — no endpoint on this host reads
// geo_entity yet, so there's no cache to warm; registered here regardless so the archetype
// domain assemblies this host is meant to carry (XD01-209/210/211) have it ready. ──────────────
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

// ── Contractor US ledger (XD01-207) — ServiceOrderDbContext (ContractorUsLedgerEntries) shares
// the same connection/database and the same two tenant interceptors as GeoAssetsDbContext above.
// Its real migrations live in GeoAssets.Server's MigrationsAssembly (XD01-202) — this host
// connects to the already-migrated schema rather than owning its own copy of the migration
// history. ────────────────────────────────────────────────────────────────────────────────────
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddDbContext<ServiceOrderDbContext>((sp, options) => options
    .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly("GeoAssets.Server"))
    .AddInterceptors(
        sp.GetRequiredService<TenantSessionInterceptor>(),
        sp.GetRequiredService<TenantTransactionInterceptor>()));
builder.Services.AddScoped<IContractorUsLedgerRepository, EFContractorUsLedgerRepository>();

// ── OpenAPI contract (XD01-207 AC) ───────────────────────────────────────────────────────────
builder.Services.AddOpenApi();

var app = builder.Build();

app.UseGeoAssetsAuthentication();
app.UseGeoAssetsObservability();

// AllowAnonymous: API documentation discoverability shouldn't itself require a bearer token —
// deliberate, minor exception to "every endpoint requires an authenticated caller by default".
app.MapOpenApi().AllowAnonymous();

// Contractor US ledger REST endpoints under /api/fieldservice/contractor-us-ledger (XD01-207) —
// see ContractorUsLedgerRestApiExtensions for the full endpoint list.
app.MapContractorUsLedgerApi();

await app.RunAsync().ConfigureAwait(false);
