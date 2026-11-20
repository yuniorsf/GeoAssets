using GeoAssets.Gateway;
using GeoAssets.Infrastructure.Observability;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

// ── Observability (XD01-384) — traces + metrics + logs → OTLP like every other host. The ASP.NET Core server span of
// each request and the HttpClient span of YARP's forwarder (whose propagator replaces the caller's traceparent with the
// forwarder span's) make a gateway hop one trace with the backend's spans; /healthz is excluded from traces. ──────────
builder.Services.AddGeoAssetsObservability(builder.Configuration);
builder.Services.ConfigureOpenTelemetryTracerProvider(tracing => tracing.AddSource(GatewayTelemetry.YarpActivitySourceName));
builder.Services.AddHealthChecks();

// ── Destination addresses (XD01-213) — read from configuration so a real deployment points
// these at its own service discovery (Kubernetes service names, Docker Compose service names,
// etc.) rather than this project hardcoding one. NeutralOperator (XD01-384) is the vertical's host — or the kernel itself
// while the kernel still hosts the module (the cut-over's rollback): the routes are the same. ─────────────────────────
var geoAssets = ReadServiceDestinations(builder.Configuration, "GeoAssets");
var ipBilling = ReadServiceDestinations(builder.Configuration, "IpBilling");
var fieldService = ReadServiceDestinations(builder.Configuration, "FieldService");
var neutralOperator = ReadServiceDestinations(builder.Configuration, "NeutralOperator");

var (routes, clusters) = GatewayRouteCatalog.Build(geoAssets, ipBilling, fieldService, neutralOperator);
builder.Services.AddReverseProxy().LoadFromMemory(routes, clusters);

// ── CORS for the browser origins (XD01-384) — see GatewayCors. ───────────────────────────────────────────────────────
builder.Services.AddGatewayCors(builder.Configuration);

// ── Per-OrganizationId token-bucket rate limiting (XD01-214) ────────────────────────────────
builder.Services.AddTenantRateLimiting(builder.Configuration);

var app = builder.Build();

// CORS first: a preflight is answered here, never rate-limited nor proxied, and a 429 still carries the CORS headers.
app.UseCors();

app.UseRateLimiter();

app.UseGeoAssetsObservability();

// The gateway authorizes nothing: every route forwards the caller's bearer token untouched to the backend that decides.
app.MapReverseProxy();

await app.RunAsync().ConfigureAwait(false);

static GatewayRouteCatalog.ServiceDestinations ReadServiceDestinations(IConfiguration configuration, string serviceName)
{
    var section = configuration.GetSection($"Destinations:{serviceName}");
    var shared = section["Shared"]
        ?? throw new InvalidOperationException($"Configuration 'Destinations:{serviceName}:Shared' not found.");

    // One entry per provisioned Tier-2 dedicated-silo tenant (XD01-289) — domain -> silo base
    // address. Empty (no "Silos" section, or none configured) is valid: every request then
    // falls through to the shared cluster above.
    var silos = section.GetSection("Silos").GetChildren()
        .ToDictionary(
            c => c.Key,
            c => c.Value ?? throw new InvalidOperationException($"Configuration 'Destinations:{serviceName}:Silos:{c.Key}' has no value."),
            StringComparer.OrdinalIgnoreCase);

    return new GatewayRouteCatalog.ServiceDestinations(shared, silos);
}
