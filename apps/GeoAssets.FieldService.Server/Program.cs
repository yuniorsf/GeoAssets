using GeoAssets.Core.Bdgd;
using GeoAssets.Core.Erp.Maximo;
using GeoAssets.Core.Interfaces;
using GeoAssets.NeutralOperator.Domain.Compliance;
using GeoAssets.NeutralOperator.Domain.Compliance.PoleAudit;
using GeoAssets.Core.Messaging;
using GeoAssets.Core.Services;
using GeoAssets.FieldService.Server;
using GeoAssets.Identity.Authentication;
using GeoAssets.Identity.Authorization;
using GeoAssets.Infrastructure.Observability;
using GeoAssets.Persistence.Postgres;
using GeoAssets.Persistence.Postgres.Interceptors;
using GeoAssets.Provider.FileGeodatabase;
using GeoAssets.Provider.Rest;
using GeoAssets.Provider.Shapefile;
using GeoAssets.Workflow;
using GeoAssets.Workflow.Agents;
using GeoAssets.Workflow.Agents.Dispatch;
using GeoAssets.FieldService.Contractors;
using GeoAssets.FieldService.Contractors.Governance;
using GeoAssets.Integrations.Erp.Totvs;
using GeoAssets.Integrations.Erp.Sienge;
using GeoAssets.FieldService.FieldWork;
using GeoAssets.Integrations.Erp.Sap;
using GeoAssets.NeutralOperator.Application.Bss;
using GeoAssets.NeutralOperator.Application.Leasing.Settlement;
using GeoAssets.NeutralOperator.Persistence;
using GeoAssets.Workflow.Messaging.Protobuf;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Persistence;
using GeoAssets.Workflow.Persistence.Outbox;
using GeoAssets.Integrations.Scada;
using GeoAssets.Workflow.SlaGuard;
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

// ── Core asset reads (XD01-386, Invariant 1 — Core Database Encapsulation) — this host never
// opens the core asset database. The geofence guard, the SCADA outage dispatch and the dynamic
// dispatch advisor read assets through narrow ports answered by GeoCoreAssetQueryAdapter over
// IGeoCoreClient (registered with the Core GeoAssets Server bridge below). ─────────────────────
builder.Services.AddScoped<GeoCoreAssetQueryAdapter>(sp => new GeoCoreAssetQueryAdapter(
    ResolveGeoCoreClient(sp),
    sp.GetRequiredService<IOrganizationScopedGeoCoreClient>(),
    sp.GetRequiredService<ITenantContext>(),
    sp.GetRequiredService<TimeProvider>()));
builder.Services.AddScoped<IAssetProximityQuery>(sp => sp.GetRequiredService<GeoCoreAssetQueryAdapter>());
builder.Services.AddScoped<IAssetTopologyQuery>(sp => sp.GetRequiredService<GeoCoreAssetQueryAdapter>());
builder.Services.AddScoped<IDispatchAssetQuery>(sp => sp.GetRequiredService<GeoCoreAssetQueryAdapter>());

// ── Contractor US ledger (XD01-207) — ServiceOrderDbContext (ContractorUsLedgerEntries, etc.)
// (XD01-290) now lives in its OWN "FieldService" database/connection string, with its own
// migration history — MigrationsAssembly points at THIS host's own assembly now
// ("GeoAssets.FieldService.Server"), not GeoAssets.Server's, since GeoAssets.Workflow.EFCore
// itself is deliberately provider-agnostic (no Npgsql package reference) and can't host
// provider-specific migration code. The one real blocker this required —
// ServiceOrders' grant-aware RLS exception needing OrganizationGrants, which now lives in a
// DIFFERENT physical database — is resolved by OrganizationGrantSyncWorker below, mirroring
// grants into this database's own OrganizationGrantMirror table (see that worker's and the
// isolation migration's own doc comments for the resulting staleness-window tradeoff). Both
// tenant interceptors are attached via the (IServiceProvider, DbContextOptionsBuilder) overload,
// so each request's own scoped ITenantContext drives TenantSessionInterceptor/
// TenantTransactionInterceptor rather than a single process-wide value (XD01-199/XD01-203; since
// XD01-386 they come from GeoAssets.Persistence.Postgres, not the core asset provider). ────────
var fieldServiceConnectionString = builder.Configuration.GetConnectionString("FieldService")
    ?? throw new InvalidOperationException(
        "Connection string 'FieldService' not found. " +
        "Set it in appsettings.json or via the ConnectionStrings__FieldService environment variable.");

// "Noisy neighbor" backstop (XD01-214) — complements the gateway's per-OrganizationId rate
// limiting: even a request the gateway let through can't hold a connection/lock open
// indefinitely on this database. Opt-in via "Postgres:StatementTimeoutMs" — unset (the default)
// leaves the connection string, and Postgres' own server-level default, untouched. Applies to
// this host's own FieldService connection string (XD01-386; it used to apply to the core one,
// which this host no longer opens).
var statementTimeoutMs = builder.Configuration.GetValue<int?>("Postgres:StatementTimeoutMs");
if (statementTimeoutMs is { } timeoutMs)
    fieldServiceConnectionString = PostgresConnectionStringHelper.WithStatementTimeout(fieldServiceConnectionString, TimeSpan.FromMilliseconds(timeoutMs));

builder.Services.AddScoped<TenantSessionInterceptor>();
builder.Services.AddScoped<TenantTransactionInterceptor>();

builder.Services.AddSingleton(TimeProvider.System);

// ── Topology-driven SCADA/OMS emergency dispatch (XD01-239) — reads the switch, its downstream
// segment and the crew roster through IAssetTopologyQuery (GeoCoreAssetQueryAdapter, XD01-386). ──
builder.Services.AddScoped<IFeederOutageDispatchService, FeederOutageDispatchService>();

// Day-to-day, yield-ranked VRPTW dispatch advisor (XD01-256) — reuses the same ICrewRoutingEngine
// XD01-240 registers for the emergency path; TryAddSingleton there/here means registering both
// is safe regardless of order.
builder.Services.AddDynamicCrewDispatchAdvisor();

builder.Services.AddDbContext<ServiceOrderDbContext>((sp, options) => options
    .UseNpgsql(fieldServiceConnectionString, npgsql => npgsql.MigrationsAssembly("GeoAssets.FieldService.Server"))
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
// FieldWorkWorkflowGraph's own doc comment) and would reject every one of them. Also registers
// the "ftth-drop-install" graph (XD01-273, first actually host-wired by XD01-280) for the same
// reason. ──────────────────────────────────────────────────────────────────────────────────────
builder.Services.AddOrderTypeRegistry(registry =>
{
    FieldWorkOrderTypes.RegisterAll(registry);
    FtthDropInstallOrderTypes.RegisterAll(registry);
});

// XD01-295 finding, deliberately not registered: SafetyChecklistSchemas.BuildElectricalOrderType
// / BuildConfinedSpaceOrderType (XD01-222) look like the obvious real types to dispatch a crew
// into so SafetyChecklistTransitionGuard has something to actually gate — but
// ValidatingServiceOrderRepository.AddAsync/UpdateAsync validate the FULL attribute schema on
// every write, including the very first AddAsync. A schema with "required"+"const: true" on
// every field (by design, XD01-222) therefore can never be satisfied at creation time, before any
// checklist has been submitted — registering either type would make it impossible to ever
// create/dispatch an order of that type at all. This is a pre-existing tension between XD01-222's
// schema mechanism and XD01-228's repository-level validation, not something this host-wiring
// ticket should silently resolve. Flagged for a follow-up ticket; the plain "field-work" type
// above (no schema) stays the only one registered, so the guard is a documented no-op until that
// follow-up lands.

// Guarded field-work transitions (XD01-295) — composes the three single-purpose guards
// (XD01-221/222/246) that already existed with zero host wiring anywhere in the repo before this
// ticket. PhotoGatingRuleRegistry starts empty (no BoQ-item photo-gating rule catalog exists yet
// in this codebase) — same "no rule = unrestricted" convention as the safety-checklist schema
// above; a host that wants real photo-gating rules calls Register on it before this point.
builder.Services.AddSingleton<PhotoGatingRuleRegistry>();
builder.Services.AddScoped<GeofenceArrivalTransitionGuard>();
builder.Services.AddScoped<SafetyChecklistTransitionGuard>();
builder.Services.AddScoped<PhotoGatingTransitionGuard>();
// TechnicianComplianceTransitionGuard (XD01-253) — fourth guard; its ITechnicianComplianceRepository
// is registered with the Totvs Protheus sync below. Fails open for a technician never synced.
builder.Services.AddScoped<TechnicianComplianceTransitionGuard>();
builder.Services.AddScoped<IFieldWorkTransitionGuard>(sp => new CompositeFieldWorkTransitionGuard(
    sp.GetRequiredService<GeofenceArrivalTransitionGuard>(),
    sp.GetRequiredService<SafetyChecklistTransitionGuard>(),
    sp.GetRequiredService<PhotoGatingTransitionGuard>(),
    sp.GetRequiredService<TechnicianComplianceTransitionGuard>()));

// ── Mobile offline-sync (XD01-208) — same ServiceOrderDbContext registration above backs
// IServiceOrderRepository too. Registered as EFServiceOrderRepository (now carrying the
// OrderTypeRegistry above) wrapped in ValidatingServiceOrderRepository (status-transition
// enforcement) — not the full ObservableServiceOrderRepository/ServiceOrderRules stack
// GeoAssets.Server wires, since this host maps no order-creation endpoint that would need
// order-type-aware creation rules (see FieldServiceSyncRestApiExtensions' own doc comment). ─────
builder.Services.AddScoped<IServiceOrderRepository>(sp =>
    new ValidatingServiceOrderRepository(
        new EFServiceOrderRepository(
            sp.GetRequiredService<ServiceOrderDbContext>(),
            orderTypeRegistry: sp.GetRequiredService<OrderTypeRegistry>(),
            timeProvider: sp.GetRequiredService<TimeProvider>(),
            hooks: sp.GetServices<IOrderLifecycleHook>()),
        sp.GetRequiredService<OrderTypeRegistry>(),
        sp.GetServices<ITransitionGuard>(),
        sp.GetRequiredService<TimeProvider>()));

// Service order targets (XD01-306): same scoped ServiceOrderDbContext as the repository above,
// which is what lets an order's closing transition release its targets in one SaveChanges.
builder.Services.AddServiceOrderTargetPersistence();

// Pole occupancy (XD01-314): in the NeutralOperator vertical's own NeutralOperatorDbContext since XD01-380 (schema
// neutral_operator, its own migration chain — apply it to this host's FieldService database after this host's own chain),
// built over the scoped ServiceOrderDbContext's connection and joining its transaction, so a commit of occupancy still
// shares one transaction with the order transition.
builder.Services.AddNeutralOperatorPersistence();

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
builder.Services.AddServiceAuthTokenProvider(builder.Configuration);
builder.Services.AddGeoCoreRestClient(coreServerBaseUrl);
// The core sees this host's service principal (no organization claim); asset reads made for a
// request with an organization go through the core's organization-scoped routes instead, so
// they see exactly what that organization sees (XD01-386, see GeoCoreAssetQueryAdapter).
builder.Services.AddOrganizationScopedGeoCoreClient(coreServerBaseUrl);
builder.Services.AddScoped<IIntegrationEventPublisher, GeoCoreBridgeEventPublisher>();

// ── OrganizationGrant sync (XD01-290) — HttpOrganizationGrantSyncClient polls Core's
// /api/identity/organization-grants/sync; OrganizationGrantSyncWorker applies each batch into
// this host's now-isolated database. Bound from config, not left at code defaults, since
// PollInterval is a real security-relevant staleness bound (see OrganizationGrantSyncOptions). ──
builder.Services.AddHttpClient(HttpOrganizationGrantSyncClient.HttpClientName);
builder.Services.AddSingleton<IOrganizationGrantSyncClient, HttpOrganizationGrantSyncClient>();
builder.Services.AddOrganizationGrantSync();
builder.Services.Configure<OrganizationGrantSyncOptions>(
    builder.Configuration.GetSection(OrganizationGrantSyncOptions.SectionName));

// ── IBM Maximo MIF asset lifecycle sync (XD01-232) — GeoCoreBridgeEventPublisher offers every
// claimed AssetMutationBatchRequestedEvent to MaximoAssetLifecycleSyncService after the Core GIS
// mutation succeeds; it's a no-op unless the batch is an asset-swap pair (one Decommission +
// one UpsertFeature, both carrying a serialNumber). Exactly-once dispatch reuses the same
// IIdempotentInbox the outbox/inbox stack above already registers. ───────────────────────────────
builder.Services.AddHttpClient(HttpMaximoAssetLifecycleDispatcher.HttpClientName);
builder.Services.AddScoped<IMaximoAssetLifecycleDispatcher, HttpMaximoAssetLifecycleDispatcher>();
builder.Services.AddScoped<MaximoAssetLifecycleSyncService>();

// ── Single-DB ACID inspection commit (XD01-228) ───────────────────────────────────────────────
builder.Services.AddScoped<InspectionCommitService>();

// ── Archetype domain modules (XD01-212) — each toggled independently via
// "Modules:{Name}:Enabled" (default true). NeutralPoleRegistry is this module's only stateful
// dependency; Electrical/Sanitation map purely stateless computation endpoints with nothing to
// register here. ─────────────────────────────────────────────────────────────────────────────
builder.Services.AddSingleton<NeutralPoleRegistry>();

// OTDR .SOR trace ingestion & emergency fault dispatch (XD01-283) — both are pure/stateless, so
// singleton is safe (same reasoning as every other stateless engine/service registered above).
builder.Services.AddSingleton<IOtdrFaultCorrelationEngine, OtdrFaultCorrelationEngine>();
builder.Services.AddSingleton<OtdrEmergencyFaultDispatchService>();

// Concessionaire compliance dossier exporter (XD01-284) — AddGeoAssetsShapefile also registers
// IShapefileExportService, which DisNor056Exporter consumes via the interface Core already owns
// (Onion layering stays intact: Core never references GeoAssets.Provider.Shapefile directly).
// Adding a third distributor's exporter is exactly one more AddSingleton<IDossierExporter, …>()
// line here — neither existing exporter nor the registry needs to change (XD01-284's AC3).
builder.Services.AddGeoAssetsShapefile();
builder.Services.AddSingleton<IDossierExporter, DisNor056Exporter>();
builder.Services.AddSingleton<IDossierExporter, JoinAsExporter>();
builder.Services.AddSingleton<DossierExporterRegistry>();

// ANEEL BDGD File Geodatabase export (XD01-225) — GDAL-backed, so server-side only (same
// native/host constraint as the PostgreSQL provider, never registered for Blazor WASM).
builder.Services.AddGeoAssetsFileGeodatabaseExport();
builder.Services.AddSingleton<BdgdFileGeodatabaseExporter>();

// Pole-sharing audit & yellow-tag legal defense evidence repository (XD01-285) — in-memory
// stateful singleton, same shape as NeutralPoleRegistry; no persistence-layer ticket exists yet.
builder.Services.AddSingleton<IPoleAuditEvidenceRepository, InMemoryPoleAuditEvidenceRepository>();

// BSS/OSS bi-directional webhook integration (XD01-280) — IXC Soft is the only adapter
// implemented (priority vendor per the ticket; no credentials/API docs available in this
// environment for MK-Auth/SGP/Voalle). Adding one of those later is exactly one more
// AddSingleton<IBssOssOrderAdapter, …>() line, same shape as the dossier exporters above.
builder.Services.AddHttpClient(HttpBssOssOutboundWebhookDispatcher.HttpClientName);
builder.Services.AddSingleton<IBssOssOrderAdapter, IxcSoftOrderAdapter>();
builder.Services.AddSingleton<BssOssOrderAdapterRegistry>();
builder.Services.AddSingleton<IBssOssOutboundWebhookDispatcher, HttpBssOssOutboundWebhookDispatcher>();
builder.Services.AddScoped<BssOrderSpawnService>();
builder.Services.AddScoped<FtthDropInstallActivationDispatchService>();

// SAP PM/S4HANA inbound work-order webhook + outbound BAPI confirmation (XD01-231) — same
// adapter/dispatcher split as the BSS/OSS integration above. IIdempotentInbox/IOutboxWriter
// (AddWorkflowOutbox, registered above) back SapPmOrderSpawnService's inbound-retry dedup.
builder.Services.AddHttpClient(HttpSapPmConfirmationDispatcher.HttpClientName);
builder.Services.AddSingleton<ISapPmOrderAdapter, SapPmOrderAdapter>();
builder.Services.AddSingleton<ISapPmConfirmationDispatcher, HttpSapPmConfirmationDispatcher>();
builder.Services.AddScoped<SapPmOrderSpawnService>();
builder.Services.AddScoped<SapPmOrderConfirmationDispatchService>();

// SlaGuardEngine's matrix is read from configuration (Sla:Matrix), never hardcoded here — the
// real statutory durations are a regulatory fact to source from the current official text (see
// SlaMatrixRow's own doc comment), the same posture XD01-226 already established. An
// unconfigured ServiceCategory/TerritorialSetting combination fails loudly (KeyNotFoundException)
// rather than silently skipping the deadline — deliberate, not an oversight.
var slaMatrix = builder.Configuration.GetSection("Sla:Matrix").Get<List<SlaMatrixRow>>() ?? [];
builder.Services.AddSingleton(new SlaGuardEngine(slaMatrix));

// Contractor governance portal integration (XD01-238) — SAP WeBUY / CEMIG Portal de
// Fornecedores. No real portal endpoint/credentials configured by default in this environment.
builder.Services.AddHttpClient(HttpContractorGovernancePortalClient.HttpClientName);
builder.Services.AddSingleton<IContractorGovernancePortalClient, HttpContractorGovernancePortalClient>();

// Totvs Protheus nightly sync (XD01-253) — technician certification/equipment-calibration pull
// (gates APR submission via TechnicianComplianceTransitionGuard, composed into the
// CompositeFieldWorkTransitionGuard registered above for the XD01-295 transition endpoint),
// approved-US-postings payroll export push, and warehouse-stock shrinkage reconciliation. The
// in-memory compliance repository is the same "no persistence ticket yet" shape as
// InMemoryPoleAuditEvidenceRepository above; the nightly sync repopulates it from scratch every
// run regardless.
builder.Services.AddSingleton<ITechnicianComplianceRepository, InMemoryTechnicianComplianceRepository>();
builder.Services.AddSingleton(builder.Configuration.GetSection(TotvsSyncRetryOptions.SectionName).Get<TotvsSyncRetryOptions>() ?? new TotvsSyncRetryOptions());
builder.Services.AddHttpClient(HttpTotvsProtheusSyncClient.HttpClientName);
builder.Services.AddScoped<ITotvsProtheusSyncClient, HttpTotvsProtheusSyncClient>();
builder.Services.AddScoped<IApprovedUsPostingSource, LedgerBackedApprovedUsPostingSource>();
builder.Services.AddScoped<IFieldMaterialConsumptionSource, LedgerBackedFieldMaterialConsumptionSource>();
builder.Services.AddScoped<TotvsNightlySyncOrchestrator>();

// Sienge nightly sync (XD01-254) — machine-hour/hauling-ticket cost-center posting push and
// purchase-order reconciliation for civil & water contractors. UsCodeClassification is
// configuration-driven (see SiengeUsCodeClassification's doc comment) rather than this
// connector guessing which US billing codes mean "equipment hour" vs "hauling ticket" vs
// "material consumed".
builder.Services.AddSingleton(builder.Configuration.GetSection(SiengeUsCodeClassification.SectionName).Get<SiengeUsCodeClassification>() ?? new SiengeUsCodeClassification());
builder.Services.AddSingleton(builder.Configuration.GetSection(SiengeSyncRetryOptions.SectionName).Get<SiengeSyncRetryOptions>() ?? new SiengeSyncRetryOptions());
builder.Services.AddHttpClient(HttpSiengeSyncClient.HttpClientName);
builder.Services.AddScoped<ISiengeSyncClient, HttpSiengeSyncClient>();
builder.Services.AddScoped<IMachineHourSource, LedgerBackedMachineHourSource>();
builder.Services.AddScoped<IHaulingTicketSource, LedgerBackedHaulingTicketSource>();
builder.Services.AddScoped<IFieldConsumptionSource, LedgerBackedFieldConsumptionSource>();
builder.Services.AddScoped<SiengeNightlySyncOrchestrator>();

// Lease-invoice settlement — NF Com issuance, PIX/Boleto payment-instrument issuance, and ERP
// posting for Poste Justo lease invoices (XD01-268). Only Pix has a shipped
// IPaymentInstrumentIssuer; see PaymentInstrumentKind's remarks for why BoletoRegistrado isn't
// registered yet. In-memory settlement-progress store, same proportionality call as
// InMemoryPoleAuditEvidenceRepository above — a durable store is a separate infra ticket.
builder.Services.AddHttpClient(HttpErpFinancialPostingClient.HttpClientName);
builder.Services.Configure<PixSettlementOptions>(builder.Configuration.GetSection(PixSettlementOptions.SectionName));
builder.Services.AddSingleton<INfComDocumentGenerator, NfComDocumentGenerator>();
builder.Services.AddSingleton<IPaymentInstrumentIssuer, PixBrCodePaymentInstrumentIssuer>();
builder.Services.AddSingleton<IErpFinancialPostingClient, HttpErpFinancialPostingClient>();
builder.Services.AddSingleton<ILeaseInvoiceSettlementRepository, InMemoryLeaseInvoiceSettlementRepository>();
builder.Services.AddSingleton<LeaseInvoiceSettlementOrchestrator>();

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

// SAP WeBUY clearing-package export under /api/fieldservice/erp/sap-webuy (XD01-255) — see
// SapWeBuyClearingPackageRestApiExtensions.
app.MapSapWeBuyClearingPackageApi();

// Mobile offline-sync endpoints under /api/field-service/sync (XD01-208) — see
// FieldServiceSyncRestApiExtensions for the full endpoint list.
app.MapFieldServiceSyncApi();

// Supervisor sign-off endpoint under /api/field-service/orders (XD01-248) — see
// FieldWorkInspectionRestApiExtensions for the full endpoint list.
app.MapFieldWorkInspectionApi();

// Crew-facing guarded field-work transition endpoint under /api/field-service/orders (XD01-295)
// — see FieldWorkTransitionRestApiExtensions for the full endpoint list.
app.MapFieldWorkTransitionApi();

// Contractor governance portal endpoints under /api/fieldservice/contractor-governance-portal
// (XD01-238) — see ContractorGovernancePortalRestApiExtensions for the full endpoint list.
app.MapContractorGovernancePortalApi();

// Evidence HMAC re-verification endpoint under /api/field-service/evidence (XD01-278) — see
// EvidenceVerificationRestApiExtensions for the full endpoint list.
app.MapEvidenceVerificationApi();

// SCADA/OMS breaker-trip ingest under /api/geoassets/topology/traverse (XD01-239) — see
// ScadaTopologyTraverseRestApiExtensions.
app.MapScadaTopologyTraverseApi();

// SAP PM/S4HANA inbound work-order webhook + outbound BAPI confirmation under
// /api/field-service/sap-pm (XD01-231) — see SapPmModuleRestApiExtensions for the full endpoint
// list. Same independently-toggled module shape as Electrical/Sanitation/NeutralPole below.
if (app.Configuration.GetValue("Modules:SapPm:Enabled", defaultValue: true))
    app.MapSapPmApi();

// Totvs Protheus nightly-sync trigger under /api/fieldservice/erp/totvs/sync (XD01-253) — see
// TotvsProtheusSyncApiExtensions for the full endpoint list.
if (app.Configuration.GetValue("Modules:Totvs:Enabled", defaultValue: true))
    app.MapTotvsProtheusSyncApi();

// Sienge nightly-sync trigger under /api/fieldservice/erp/sienge/sync (XD01-254) — see
// SiengeSyncApiExtensions for the full endpoint list.
if (app.Configuration.GetValue("Modules:Sienge:Enabled", defaultValue: true))
    app.MapSiengeSyncApi();

// Day-to-day dynamic dispatch advisory preview under /api/geoassets/dispatch/advisory-plan
// (XD01-256) — see DynamicDispatchAdvisoryRestApiExtensions.
app.MapDynamicDispatchAdvisoryApi();

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
{
    app.MapIspModuleApi();

    // CTO splitter port capacity tracking & occupancy heatmap (XD01-281) — same module toggle,
    // registered right alongside the scaffolding it builds on.
    app.MapIspCtoOccupancyApi();

    // OTDR .SOR trace ingestion & emergency fault dispatch (XD01-283).
    app.MapIspOtdrFaultDispatchApi();

    // Concessionaire compliance dossier exporter (XD01-284).
    app.MapIspComplianceDossierApi();

    // Pole-sharing audit & yellow-tag legal defense evidence repository (XD01-285).
    app.MapIspPoleAuditApi();

    // Emergency legal defense dossier generator (XD01-286).
    app.MapIspLegalDefenseDossierApi();

    // FTTH drop-install order transitions (XD01-280) — the BSS/OSS activation-webhook trigger.
    app.MapFtthDropInstallApi();

    // Lease-invoice settlement run trigger + paid-webhook reconciliation (XD01-268).
    app.MapLeaseInvoiceSettlementApi();
}

await app.RunAsync().ConfigureAwait(false);

// IGeoCoreClientResolver when registered — routed to the shard of the request's organization —
// else the plain IGeoCoreClient (XD01-386).
static IGeoCoreClient ResolveGeoCoreClient(IServiceProvider sp) =>
    sp.GetService<IGeoCoreClientResolver>() is { } resolver
        ? resolver.Resolve(organizationId: sp.GetService<ITenantContext>()?.CurrentOrganizationId ?? Guid.Empty)
        : sp.GetRequiredService<IGeoCoreClient>();
