using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Messaging;
using GeoAssets.FieldService.Server;
using GeoAssets.Identity.Authentication;
using GeoAssets.Infrastructure.Observability;
using GeoAssets.Provider.PostgreSQL;
using GeoAssets.Provider.PostgreSQL.Data;
using GeoAssets.Provider.PostgreSQL.Interceptors;
using GeoAssets.Provider.Rest;
using GeoAssets.Workflow;
using GeoAssets.Workflow.Contractors;
using GeoAssets.Workflow.FieldWork;
using GeoAssets.Workflow.Messaging.Protobuf;
using GeoAssets.Workflow.Orders;
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

// "Noisy neighbor" backstop (XD01-214) — complements the gateway's per-OrganizationId rate
// limiting: even a request the gateway let through can't hold a connection/lock open
// indefinitely on this shared database. Opt-in via "Postgres:StatementTimeoutMs" — unset (the
// default) leaves the connection string, and Postgres' own server-level default, untouched.
var statementTimeoutMs = builder.Configuration.GetValue<int?>("Postgres:StatementTimeoutMs");
if (statementTimeoutMs is { } timeoutMs)
    connectionString = PostgresConnectionStringHelper.WithStatementTimeout(connectionString, TimeSpan.FromMilliseconds(timeoutMs));

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

// ── Contractor US postings + catalog (XD01-242/XD01-247/XD01-228) — same ServiceOrderDbContext,
// same per-request tenant interceptors. ──────────────────────────────────────────────────────
builder.Services.AddScoped<IUsCatalogRepository, EFUsCatalogRepository>();
builder.Services.AddScoped<IContractorUsPostingRepository, EFContractorUsPostingRepository>();
builder.Services.AddScoped<IPavingMeasurementRepository, EFPavingMeasurementRepository>();

// ── OrderType registry (XD01-228) — registered (with the "field-work" graph) so
// IServiceOrderRepository below can actually validate a transition into any FieldWork state
// (Inspected included); without it, ServiceOrderTransitions.IsValid would only ever consult the
// global default graph, which doesn't know these states at all (see
// FieldWorkWorkflowGraph's own doc comment) and would reject every one of them. ────────────────
builder.Services.AddOrderTypeRegistry(FieldWorkOrderTypes.RegisterAll);

// ── Mobile offline-sync (XD01-208) — same ServiceOrderDbContext registration above backs
// IServiceOrderRepository too. Registered as EFServiceOrderRepository (now carrying the
// OrderTypeRegistry above) wrapped in ValidatingServiceOrderRepository (status-transition
// enforcement) — not the full ObservableServiceOrderRepository/ServiceOrderRules stack
// GeoAssets.Server wires, since this host maps no order-creation endpoint that would need
// order-type-aware creation rules (see FieldServiceSyncRestApiExtensions' own doc comment). ─────
builder.Services.AddScoped<IServiceOrderRepository>(sp =>
    new ValidatingServiceOrderRepository(
        new EFServiceOrderRepository(sp.GetRequiredService<ServiceOrderDbContext>(), orderTypeRegistry: sp.GetRequiredService<OrderTypeRegistry>()),
        sp.GetRequiredService<OrderTypeRegistry>()));

// ── Transactional outbox + relay dispatcher (XD01-228/XD01-229) — AddWorkflowOutbox wires
// IOutboxWriter, IOutboxDispatcherStore, IIdempotentInbox, and the background
// OutboxRelayProcessor hosted service (claim → publish → mark dispatched/failed with
// exponential backoff). Protobuf chosen as the wire serializer since
// AssetMutationBatchRequestedEvent (the only message type this host's outbox stages, from
// InspectionCommitService) already has a ProtobufIntegrationMessageSerializer mapping — see
// that type's own switch cases. ───────────────────────────────────────────────────────────────
builder.Services.AddWorkflowProtobufMessaging();
builder.Services.AddWorkflowOutbox();

// ── Core GeoAssets Server bridge (XD01-229) — IIntegrationEventPublisher that
// OutboxRelayProcessor calls per claimed message; GeoCoreBridgeEventPublisher turns that into a
// direct HTTP call to Core's /api/geoassets/mutation-batches endpoint instead of a real broker
// (this outbox has exactly one consumer, Core itself, so a broker hop buys nothing here). ───────
var coreServerBaseUrl = builder.Configuration["GeoCoreServer:BaseUrl"]
    ?? throw new InvalidOperationException(
        "Configuration 'GeoCoreServer:BaseUrl' not found. " +
        "Set it in appsettings.json or via the GeoCoreServer__BaseUrl environment variable.");
builder.Services.AddGeoCoreRestClient(coreServerBaseUrl);
builder.Services.AddScoped<IIntegrationEventPublisher, GeoCoreBridgeEventPublisher>();

// ── Single-DB ACID inspection commit (XD01-228) ───────────────────────────────────────────────
builder.Services.AddScoped<InspectionCommitService>();

// ── Archetype domain modules (XD01-212) — each toggled independently via
// "Modules:{Name}:Enabled" (default true). NeutralPoleRegistry is this module's only stateful
// dependency; Electrical/Sanitation map purely stateless computation endpoints with nothing to
// register here. ─────────────────────────────────────────────────────────────────────────────
builder.Services.AddSingleton<NeutralPoleRegistry>();

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

// Mobile offline-sync endpoints under /api/field-service/sync (XD01-208) — see
// FieldServiceSyncRestApiExtensions for the full endpoint list.
app.MapFieldServiceSyncApi();

// Supervisor sign-off endpoint under /api/field-service/orders (XD01-248) — see
// FieldWorkInspectionRestApiExtensions for the full endpoint list.
app.MapFieldWorkInspectionApi();

// Evidence HMAC re-verification endpoint under /api/field-service/evidence (XD01-278) — see
// EvidenceVerificationRestApiExtensions for the full endpoint list.
app.MapEvidenceVerificationApi();

// Archetype domain modules (XD01-212) — independently toggled, independently mapped. A module
// left disabled (or one whose own registration fails) never prevents another from mapping: each
// MapXModuleApi() call only ever adds its own routes, so there's nothing shared to break.
if (app.Configuration.GetValue("Modules:Electrical:Enabled", defaultValue: true))
    app.MapElectricalModuleApi();

if (app.Configuration.GetValue("Modules:Sanitation:Enabled", defaultValue: true))
    app.MapSanitationModuleApi();

if (app.Configuration.GetValue("Modules:NeutralPole:Enabled", defaultValue: true))
    app.MapNeutralPoleModuleApi();

// ISP vertical host scaffolding (XD01-279) — webhook ingestion shell + IGeoCoreClient proxy that
// XD01-280/281/283/284 register their real logic behind. Same independently-toggled module shape
// as Electrical/Sanitation/NeutralPole above.
if (app.Configuration.GetValue("Modules:Isp:Enabled", defaultValue: true))
    app.MapIspModuleApi();

await app.RunAsync().ConfigureAwait(false);
