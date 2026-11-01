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
