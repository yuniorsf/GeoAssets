using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Services;
using GeoAssets.Hosting.Abstractions;
using GeoAssets.Identity.Authentication;
using GeoAssets.Infrastructure.Observability;
using GeoAssets.NeutralOperator.Modules;
using GeoAssets.NeutralOperator.Server;
using GeoAssets.Provider.Rest;
using GeoAssets.Workflow;
using GeoAssets.Workflow.Hosting;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rest;
using GeoAssets.Workflow.Targets;

var builder = WebApplication.CreateBuilder(args);

// ── Observability (traces + metrics + logs → OTLP) and the /healthz liveness endpoint (UseGeoAssetsObservability). ──
builder.Services.AddGeoAssetsObservability(builder.Configuration);
builder.Services.AddSingleton(TimeProvider.System);

// ── Authentication (bearer tokens of the CIAM tenant, the kernel's API audience) — every endpoint requires an
// authenticated caller by default (XD01-12). ──────────────────────────────────────────────────────────────────────
builder.Services.AddGeoAssetsAuthentication(builder.Configuration);

// ── Tenant resolution (XD01-200) — the request's organization claim stamps this host's row-level security. ────────
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserAccessor>(sp =>
    new ClaimsPrincipalCurrentUserAccessor(() => sp.GetRequiredService<IHttpContextAccessor>().HttpContext?.User));
builder.Services.AddScoped<ITenantOrganizationScope, TenantOrganizationScope>();
builder.Services.AddScoped<ITenantContext, HttpTenantContext>();

// ── The kernel (GeoCoreServer:BaseUrl = its root, e.g. http://geoassets-server:8080) — reached only over REST. ──────
var kernelBaseUrl = builder.Configuration["GeoCoreServer:BaseUrl"]
    ?? throw new InvalidOperationException(
        "Configuration 'GeoCoreServer:BaseUrl' not found. " +
        "Set it to the kernel's root URL in appsettings.json or via the GeoCoreServer__BaseUrl environment variable.");
var kernelBaseAddress = new Uri(kernelBaseUrl.TrimEnd('/') + "/", UriKind.Absolute);

// Machine to machine as GeoAssets.ServiceCaller (XD01-299): features (organization-scoped routes, XD01-386), the global
// target ledger (XD01-379) and the bootstrap registration (XD01-369/XD01-377). The organization is always named in the
// route or the body of these routes, never in a header.
builder.Services.AddServiceAuthTokenProvider(builder.Configuration);
builder.Services.AddGeoCoreRestClient(new Uri(kernelBaseAddress, "api/geoassets").ToString());
builder.Services.AddOrganizationScopedGeoCoreClient(new Uri(kernelBaseAddress, "api/geoassets").ToString());
builder.Services.AddKernelServiceCallerClients(kernelBaseAddress.ToString());

// The caller's own identity, as the kernel answers it for the caller's token (ADR-001 D3): the WorkflowPrincipal every
// ServiceOrderRules decision is made for, and the RequireAuthorization("…") permission policies.
builder.Services.AddKernelIdentity(kernelBaseAddress);
builder.Services.AddGeoPermissionPolicies();

// ── This host's own workflow store (database geoassets_neutral_operator; migrations in this assembly). ──────────────
var connectionString = builder.Configuration.GetConnectionString(NeutralOperatorServerPersistence.ConnectionStringName)
    ?? throw new InvalidOperationException(
        $"Connection string '{NeutralOperatorServerPersistence.ConnectionStringName}' not found. " +
        "Set it in appsettings.json or via the ConnectionStrings__NeutralOperator environment variable.");
builder.Services.AddOrderTypeRegistry();
builder.Services.AddNeutralOperatorServerWorkflowPersistence(connectionString);

// ── The generic engine the module runs on: rules (the one service-order authorization point), their ASP.NET Core
// requirements, action-handler validation, the workflow outbox. ────────────────────────────────────────────────────
builder.Services.AddServiceOrderRules();
builder.Services.AddServiceOrderAuthorization();
builder.Services.AddHostedService<OrderActionHandlerValidation>();
builder.Services.AddServerWorkflowMessaging(builder.Configuration);
builder.Services.AddSingleton(NeutralOperatorMessaging.From(builder.Configuration));

// The approval saga (XD01-383) needs a broker on this host: the kernel that applies its asset mutation batches is remote.
if (!WorkflowMessagingHostExtensions.HasBroker(builder.Configuration))
    builder.Services.AddHostedService<ApprovalSagaTransportWarning>();

// ── Targets: selection is the kernel's ledger's (ADR-001 §3.4 D2) — LedgerOrderTargetSelectionService reserves there and
// mirrors the reservation into this host's ServiceOrderTargets; LedgerTargetReleaseLifecycleHook releases it when the
// order closes. The strategy catalog stays local only to word the routes' problems; nothing here queries features. ──
builder.Services.AddSingleton<IAssetSpatialQuery, KernelSelectionOnlySpatialQuery>();
builder.Services.AddFeatureSelection();
builder.Services.AddScoped<IOrderTargetSelectionService, LedgerOrderTargetSelectionService>();
builder.Services.AddScoped<IOrderLifecycleHook, LedgerTargetReleaseLifecycleHook>();

// ── The NeutralOperator module (always enabled here): order types, descriptors, permissions, occupancy staging and
// commit, shared-use dispatch, clandestine-notice actions, and its NeutralOperatorDbContext over this host's connection. ─
await builder.AddGeoAssetsModulesAsync(NeutralOperatorServerModules.BuiltIns()).ConfigureAwait(false);

// ── Bootstrap registration with the kernel, retried with backoff; its state is the vertical-registration check: Degraded
// (→ /healthz 200) until registered, and /readyz answers 503 until then. ──────────────────────────────────────────────
builder.Services.AddVerticalRegistration(
    builder.Configuration,
    new VerticalRegistrationSource(
        NeutralOperatorModule.ModuleId,
        sp => NeutralOperatorModule.PublishedDescriptors(sp.GetRequiredService<OrderTypeRegistry>())));

// ── CORS — the Blazor WASM origins configured in appsettings.json, as the kernel. ────────────────────────────────────
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(opts => opts.AddDefaultPolicy(p => p.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

// Overlay any DB-persisted OrderTypes on the seeded defaults and the module's types, as the kernel does.
await app.Services.LoadRegistryFromDbAsync().ConfigureAwait(false);

app.UseCors();
app.UseGeoAssetsAuthentication();
app.UseGeoAssetsModules();
app.UseGeoAssetsObservability();

// Readiness: Healthy only once the kernel knows this vertical (every check tagged "ready"); 503 otherwise.
app.MapVerticalReadiness();

// The chassis' generic service-order, order-type, target and selection routes under /api/workflow — the same routes the
// kernel serves for its in-process orders. Map gestures are resolved by the kernel's own /selection/resolve.
app.MapServiceOrdersApi();
app.MapServiceOrderTargetsApi(mapSelectionResolve: false);

// The module's own routes: occupancy staging and shared-use dispatch.
app.MapGeoAssetsModules();

await app.RunAsync().ConfigureAwait(false);
