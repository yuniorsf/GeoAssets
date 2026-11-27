using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Services;
using GeoAssets.Core.Providers;
using GeoAssets.Core.Rendering;
using GeoAssets.Hosting.Abstractions;
using GeoAssets.Hosting.Abstractions.Plugins;
using GeoAssets.Identity;
using GeoAssets.Identity.Authentication;
using GeoAssets.Infrastructure.Observability;
using GeoAssets.Projects;
using GeoAssets.Server;
using GeoAssets.Server.Catalogs;
using GeoAssets.Server.Modules;
using GeoAssets.Server.Rendering;
using GeoAssets.Workflow;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Registry;
using GeoAssets.Workflow.Rules;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;

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

// ── Clock ─────────────────────────────────────────────────────────────────────
// The asset store (PostgreSQL/PostGIS: provider, organization catalogs, spatial query, WMS
// renderer) is a module since XD01-359 — see PostgresSpatialModule, enabled below.
builder.Services.AddSingleton(TimeProvider.System);

// ── Service Order workflow persistence (XD01-8) — same Postgres database/connection
// as assets; workflow tables live alongside geo_entity/asset_type. OrderTypes seeded
// in-code by default, then overlaid with any DB-persisted types after the host builds
// (see LoadRegistryFromDbAsync below). Modules add their own order types to this registry
// (ConfigureOrderTypeRegistry) when AddGeoAssetsModules runs below.
builder.Services.AddOrderTypeRegistry();
builder.Services.AddWorkflowPersistence(o => o.UseNpgsql(
    connectionString,
    // ServiceOrderDbContext lives in GeoAssets.Workflow.EFCore (kept provider-agnostic —
    // see its own doc comment), so migrations targeting Postgres are generated into this
    // (Server) project instead, which already legitimately depends on Npgsql.
    npgsql => npgsql.MigrationsAssembly("GeoAssets.Server")));
// The grant-aware read path (XD01-328), so GET /api/workflow/service-orders also lists the orders the caller's grants
// reach (XD01-408) — see GrantedServiceOrderList.
builder.Services.AddCrossOrganizationServiceOrderReader();

// ── Project persistence (XD01-139) — its own ProjectDbContext (control-plane metadata,
// not folded into GeoAssetsDbContext), same Postgres database/connection as everything else.
builder.Services.AddProjectPersistence(o => o.UseNpgsql(
    connectionString,
    // ProjectDbContext lives in GeoAssets.Projects.EFCore (kept provider-agnostic, same
    // reasoning as ServiceOrderDbContext above), so migrations targeting Postgres are
    // generated into this (Server) project instead.
    npgsql => npgsql.MigrationsAssembly("GeoAssets.Server")));

// IProjectService (XD01-141) — copy-on-write fork-on-edit authorization for Project
// mutations, layered on top of IProjectRepository above.
builder.Services.AddScoped<IProjectService, ProjectService>();

// OrganizationGrantService (XD01-385) — the generic grant upsert/revoke/list behind MapOrganizationGrantsApi.
builder.Services.AddScoped<OrganizationGrantService>();

// IssuedOrganizationGrantService (XD01-411) — an organization's administrators issue, edit and revoke the grants it
// issues, behind MapOrganizationIssuedGrantsApi.
builder.Services.AddScoped<IssuedOrganizationGrantService>();

// ServiceOrderRules (XD01-16) — same singleton, role-grant configuration as
// apps/GeoAssets.Web/Program.cs, so server-side enforcement and the client UI agree on
// who can do what. ServerWorkflowPrincipalFactory builds the WorkflowPrincipal it evaluates
// against from the authenticated caller (IGeoAuthorizationService, registered below).
builder.Services.AddServiceOrderRules();
builder.Services.AddScoped<ServerWorkflowPrincipalFactory>();
// XD01-364: ServiceOrderRules as ASP.NET Core requirements — the one decision point for a route that acts on an order.
builder.Services.AddServiceOrderAuthorization();
// Fails startup when two modules' order action handlers claim the same (order type, action) (XD01-360).
builder.Services.AddHostedService<OrderActionHandlerValidation>();

// Service-order targets from a selection (XD01-308): the strategy registry (registered in no
// host before this), the selection service over the target store AddWorkflowPersistence
// registered above and the IAssetSpatialQuery registered with the Postgres provider, and its
// span/metric decorator.
builder.Services.AddFeatureSelection();
// The generic IAssetSpatialQuery over IAssetProvider (ADR-001 Part 1.1): what the kernel scans with
// when no module registers a pushdown; PostgresSpatialModule's PostGIS query, registered later, wins.
builder.Services.TryAddScoped<IAssetSpatialQuery, AssetProviderSpatialQuery>();
// The generic IAssetRenderQuery (XD01-372) over the request's IAssetProvider: what WMS renders with when no module
// registers a pushdown; PostgresSpatialModule's PostGIS render query replaces it.
builder.Services.TryAddScoped<IAssetRenderQuery>(sp => new AssetProviderRenderQuery(sp.GetRequiredService<IAssetProvider>()));
// Raster tiles (XD01-433): the SkiaSharp source over whichever IAssetRenderQuery the request resolves — the PostGIS
// pushdown with postgres-spatial, the generic query above otherwise.
builder.Services.AddSkiaRasterTileSource();
// The generic IReferenceFeatureQuery (XD01-330) over the organization catalogs; PostgresSpatialModule's PostGIS query replaces it.
builder.Services.TryAddScoped<IReferenceFeatureQuery, OrganizationCatalogReferenceFeatureQuery>();
builder.Services.AddOrderTargets();
builder.Services.AddObservableOrderTargetSelection();

// Workflow integration-event transport (XD01-357): the transactional outbox every order workflow
// stages its integration events on, in the same transaction as the change they report. The relay
// publishes only when Workflow:Outbox:Transport names a broker; see
// WorkflowMessagingHostExtensions.AddServerWorkflowMessaging for what happens to messages until then.
builder.Services.AddServerWorkflowMessaging(builder.Configuration);

// ── Asset catalogs (XD01-371, ADR-001 Phase 4) — the named catalogs of GeoAssets:Providers, each built by the provider
// plugin it names once the host is built (InitializeCatalogPoolAsync below); the "primary" one is the host's
// IAssetProvider. Registered before the modules: they contribute the plugins, and may scope that IAssetProvider.
builder.Services.AddServerCatalogPool(builder.Configuration);

// ── Drop-in provider plugins (XD01-428, ADR-002 D2) — the plugins of GeoAssets:ProviderPlugins:Directory (default
// plugins/providers, skipped while absent), each in its own load context. The kernel's own "postgres" plugin stays the
// postgres-spatial module's; a dropped-in plugin with an id already registered fails the startup.
builder.Services.AddProviderPluginsFromDirectory(builder.Configuration, ProviderHostTargets.Server);

// ── Order-type registry (XD01-369) — the verticals' order-type descriptors (feature actions as data); the in-process
// modules contribute theirs while they are enabled below, remote verticals PUT theirs.
builder.Services.AddOrderTypeDescriptorRegistry();

// ── Global target ledger (XD01-379, ADR-001 §3.4) — remote verticals' order targets keyed by (vertical, orderId), so
// exclusivity holds across verticals; with a broker, the verticals' OrderStateChangedV1 releases a closed order's targets.
builder.Services.AddServerTargetLedger(builder.Configuration);

// ── Modules (XD01-358/XD01-359, ADR-001 Phase 1) — the ones listed in GeoAssets:Modules, in that
// order; with none the kernel boots on its own (invariant I5). The built-ins are ServerModules'
// (postgres-spatial, neutral-operator): the default lists only 'postgres-spatial' (XD01-385); listing
// 'neutral-operator' again is the rollback of the cut-over to GeoAssets.NeutralOperator.Server (XD01-382).
// An unknown id (e.g. the retired 'ip-billing-bridge') fails the startup, naming the known ids.
await builder.AddGeoAssetsModulesAsync(ServerModules.BuiltIns()).ConfigureAwait(false);

// ── Asset-mutation apply (XD01-383, ADR-001 Phase 7) — the kernel's one consumer of every vertical's
// AssetMutationBatchRequestedV1: applied once to this host's catalog, answered with AppliedV1/RejectedV1 on the outbox.
// Over the broker when one names a topic for it, in process (this host's relay) when there is none. After the modules:
// the provider module may register the catalog's own applier.
builder.Services.AddServerAssetMutationApply(builder.Configuration);

// Organization-scoped asset reads (XD01-373): when no module registered a native IOrganizationCatalogFactory, each
// request's IAssetProvider is its organization's filtering view of the default catalog.
builder.Services.AddServerOrganizationAssetCatalogs();

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

// ITenantContext (XD01-200) — resolves GeoAssetsDbContext's row-level tenant scoping from the
// same per-request ICurrentUserAccessor above, replacing the NullTenantContext default.
builder.Services.AddScoped<ITenantContext, HttpTenantContext>();
builder.Services.AddScoped<ITenantOrganizationScope, TenantOrganizationScope>();

// The caller's organization from identity membership when the token carries none (XD01-471); resolved per request only
// when GeoAssets:Tenancy:OrganizationFromMembership is on (UseCallerMembershipOrganization below).
builder.Services.AddCallerMembershipOrganization();

// AuthZ bridge (XD01-13) — lets endpoints declare .RequireAuthorization("SomePolicyName")
// (matching an AppPolicy.Name row) and get evaluated against IGeoAuthorizationService,
// the same policy engine the Blazor client already uses.
builder.Services.AddGeoAuthorizationPolicyBridge();

// Role assignment provider (XD01-59 Phase 2) — no-op until XD01-62 lands a real,
// Graph-backed provider to pass here.
builder.Services.AddRoleAssignmentProvider(builder.Configuration);

// User invitation provider (XD01-59 Phase 3) — no-op until Invitation:Enabled configures the
// real, Graph-backed provider (XD01-67), reusing the RoleSync credential above.
builder.Services.AddUserInvitationProvider(builder.Configuration);

// Invitation email sender (XD01-59 Phase 3) — no-op until Invitation:Enabled configures the
// real, ACS-backed sender (XD01-68).
builder.Services.AddInvitationEmailSender(builder.Configuration);

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

// ── Response compression (XD01-430): Brotli/gzip for JSON/XML bodies ──────────
builder.Services.AddKernelResponseCompression();

// ── Build ─────────────────────────────────────────────────────────────────────
var app = builder.Build();

// Build the asset catalogs (migrating and warming the Postgres one) before anything reads assets.
await app.Services.InitializeCatalogPoolAsync().ConfigureAwait(false);

// Overlay any DB-persisted OrderTypes on top of the seeded defaults.
await app.Services.LoadRegistryFromDbAsync().ConfigureAwait(false);

// Seed canonical identity roles/permissions/policies (idempotent, XD01-14).
await app.Services.SeedGeoIdentityAsync().ConfigureAwait(false);

// Seed a default Project per organization (idempotent, XD01-144) — must run after identity
// seeding above, since it needs the organizations that seeder creates.
await app.Services.SeedDefaultProjectsAsync().ConfigureAwait(false);

// First, so every body below it — the feature reads' GeoJSON above all — goes out compressed when the caller accepts it.
app.UseResponseCompression();

app.UseCors();

app.UseGeoAssetsAuthentication();

// After authorization, before the modules' middleware warms the caller's organization catalog (XD01-471).
app.UseCallerMembershipOrganization(app.Configuration);

// Module middleware, after authentication (XD01-359) — e.g. PostgresSpatialModule loads the
// caller's organization's asset catalog before its endpoint runs (XD01-338).
app.UseGeoAssetsModules();

app.UseGeoAssetsObservability();

// Expose all GeoAssets REST endpoints under /api/geoassets
// Endpoints: GET/POST /features, GET/PUT/DELETE /features/{id},
//            POST /features/bulk, POST /features/load, DELETE /features,
//            GET/POST /asset-types, DELETE /asset-types/{id},
//            GET/POST /layers, DELETE /layers/{id},
//            GET/POST /layer-rules (GET takes optional ?assetTypeId=), DELETE /layer-rules/{id}
app.MapGeoAssetsApi();

// Organization-scoped feature reads for trusted service callers (XD01-386) — a vertical host's
// service principal names the organization it reads on behalf of; see
// OrganizationScopedFeaturesRestApiExtensions.
app.MapOrganizationScopedFeaturesApi();

// Chunked bulk feature imports for trusted service callers, over the IFeatureBulkImporter the postgres-spatial module
// registers — see FeatureImportRestApiExtensions.
app.MapFeatureImportApi();

// Service Order + Order Type REST endpoints under /api/workflow (XD01-8) —
// see ServiceOrdersRestApiExtensions for the full endpoint list.
app.MapServiceOrdersApi();

// Service-order target + selection endpoints under /api/workflow (XD01-308) —
// see ServiceOrderTargetsRestApiExtensions for the full endpoint list.
app.MapServiceOrderTargetsApi();

// The global target ledger's machine-to-machine routes under /api/targets/ledger (XD01-379) —
// see TargetLedgerRestApiExtensions.
app.MapTargetLedgerApi();

// Project REST endpoints under /api/projects (XD01-141) —
// see ProjectsRestApiExtensions for the full endpoint list.
app.MapProjectsApi();

// Read-only identity/authorization endpoints under /api/identity (XD01-18) — see
// IdentityRestApiExtensions for the full endpoint list.
app.MapIdentityApi();

// The caller's own organization's active cross-organization grants, under the same
// /api/identity prefix (XD01-332) — what lets a client evaluate the service-order
// organization gate as this host does. See OwnOrganizationGrantsRestApiExtensions.
app.MapOwnOrganizationGrantsApi();

// The caller's own cross-organization entitlements — its organization's grants and its scoped roles, with the
// organizations' names (XD01-329) — under the same prefix. See CrossOrgEntitlementsRestApiExtensions.
app.MapCrossOrgEntitlementsApi();

// The generic organization-grant API (XD01-385), under the same prefix: vertical hosts upsert, revoke and list the
// cross-organization grants their reference data implies, as GeoAssets.ServiceCaller. See OrganizationGrantsRestApiExtensions.
app.MapOrganizationGrantsApi();

// The roles users of contracted organizations hold on an organization (XD01-327), under the same prefix: an organization's
// administrator assigns and revokes them. See OrganizationScopedRolesRestApiExtensions.
app.MapOrganizationScopedRolesApi();

// The grants an organization issues to its contractors, written by its administrators (XD01-411), under the same prefix.
// Grants a vertical host provisioned stay read-only there. See OrganizationIssuedGrantsRestApiExtensions.
app.MapOrganizationIssuedGrantsApi();

// Standalone OGC endpoint for external GIS clients (CORS not required for server-to-server or
// native desktop tools). The WMS routes (/wms, /api/geoassets/wms) render from PostGIS and are
// PostgresSpatialModule's (XD01-359).
app.MapWfsApi();  // GET /wfs — OGC WFS 2.0

// The order-type registry and the feature actions it resolves (XD01-369) — see OrderTypeRegistryRestApiExtensions.
app.MapOrderTypeRegistryApi();

// The enabled modules' endpoints, after the kernel's own (XD01-358).
app.MapGeoAssetsModules();

await app.RunAsync().ConfigureAwait(false);
