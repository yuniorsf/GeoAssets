using GeoAssets.Gateway;

var builder = WebApplication.CreateBuilder(args);

// ── Destination addresses (XD01-213) — read from configuration so a real deployment points
// these at its own service discovery (Kubernetes service names, Docker Compose service names,
// etc.) rather than this project hardcoding one. ───────────────────────────────────────────────
var geoAssets = ReadServiceDestinations(builder.Configuration, "GeoAssets");
var ipBilling = ReadServiceDestinations(builder.Configuration, "IpBilling");
var fieldService = ReadServiceDestinations(builder.Configuration, "FieldService");

var (routes, clusters) = GatewayRouteCatalog.Build(geoAssets, ipBilling, fieldService);
builder.Services.AddReverseProxy().LoadFromMemory(routes, clusters);

// ── Per-OrganizationId token-bucket rate limiting (XD01-214) ────────────────────────────────
builder.Services.AddTenantRateLimiting(builder.Configuration);

var app = builder.Build();

app.UseRateLimiter();

app.MapReverseProxy();

await app.RunAsync().ConfigureAwait(false);

static GatewayRouteCatalog.ServiceDestinations ReadServiceDestinations(IConfiguration configuration, string serviceName)
{
    var section = configuration.GetSection($"Destinations:{serviceName}");
    var shared = section["Shared"]
        ?? throw new InvalidOperationException($"Configuration 'Destinations:{serviceName}:Shared' not found.");
    var silo = section["Silo"]
        ?? throw new InvalidOperationException($"Configuration 'Destinations:{serviceName}:Silo' not found.");

    return new GatewayRouteCatalog.ServiceDestinations(shared, silo);
}
