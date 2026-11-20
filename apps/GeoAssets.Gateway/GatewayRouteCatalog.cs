using Yarp.ReverseProxy.Configuration;

namespace GeoAssets.Gateway;

/// <summary>
/// Builds the gateway's route/cluster table (XD01-213, per-tenant silo selection XD01-289, the vertical routes and the
/// kernel catch-all XD01-384): for each proxied path, one dedicated-silo route per provisioned tenant domain
/// (<see cref="TenantDomainHeader"/> matched against that domain's exact value, case-insensitively, each at a lower
/// <c>Order</c> than the shared route — lower runs first in YARP's precedence) and a shared multi-tenant route matching
/// the identical path pattern with no header condition, so a silo tenant's request is routed to its own cluster even
/// though all routes match the same path, while a request carrying no domain, or a domain with no provisioned silo, falls
/// through to the shared cluster rather than an arbitrary/wrong tenant's silo.
/// <list type="bullet">
///   <item><c>/api/geoassets/**</c>, <c>/api/ip-billing/**</c>, <c>/api/field-service/**</c> — forwarded as they are.</item>
///   <item><see cref="NeutralOperatorPathPrefix"/><c>/**</c> — the NeutralOperator vertical (ADR-001 Phase 7), its prefix
///   rewritten to <c>/api/**</c> (query string kept): <c>/api/v/neutral-operator/workflow/service-orders</c> reaches the
///   destination's <c>/api/workflow/service-orders</c>. The destination is configuration — <c>NeutralOperator.Server</c>
///   in production, the kernel itself while it still hosts the module (the rollback), whose routes are the same.</item>
///   <item>The kernel catch-all — every other <c>/api/**</c> and the OGC <c>/wms</c>/<c>/wfs</c> endpoints, at the
///   highest <c>Order</c> (lowest precedence) of the table, so the kernel's APIs answer through the gateway unchanged.</item>
/// </list>
/// Every route forwards the caller's <c>Authorization</c> header untouched (the gateway authorizes nothing; each backend
/// validates the token and decides), sets <c>X-Forwarded-For/Proto/Host/Prefix</c> (overwriting what the caller sent),
/// drops every <see cref="UntrustedOrganizationHeaders"/> header (a backend never takes the organization from a header;
/// dropping it here is defence in depth) and drops the backends' own CORS response headers (<see cref="BackendCorsResponseHeaders"/>):
/// the gateway is the one CORS authority the browser talks to, so a backend's duplicate can never produce a second
/// <c>Access-Control-Allow-Origin</c>.
/// </summary>
internal static class GatewayRouteCatalog
{
    public const string TenantDomainHeader = "X-Tenant-Domain";

    /// <summary>The NeutralOperator vertical's public prefix (ADR-001: <c>/api/v/{vertical}/…</c>).</summary>
    public const string NeutralOperatorPathPrefix = "/api/v/neutral-operator";

    /// <summary>
    /// The <c>Order</c> the kernel catch-all's routes start at: above every other route of the table (theirs count from 1,
    /// one per provisioned silo plus the shared route), so the catch-all only answers what no other route matches.
    /// </summary>
    public const int KernelCatchAllFirstOrder = 10_000;

    /// <summary>Organization headers no caller may hand a backend: dropped from every proxied request.</summary>
    public static readonly IReadOnlyList<string> UntrustedOrganizationHeaders = ["X-GeoAssets-Organization-Id", "X-Organization-Id"];

    /// <summary>The CORS response headers dropped from every backend response; the gateway's own CORS policy sets them.</summary>
    public static readonly IReadOnlyList<string> BackendCorsResponseHeaders =
    [
        "Access-Control-Allow-Origin",
        "Access-Control-Allow-Credentials",
        "Access-Control-Allow-Headers",
        "Access-Control-Allow-Methods",
        "Access-Control-Expose-Headers",
        "Access-Control-Max-Age",
    ];

    private const string CatchAll = "{**catch-all}";

    /// <summary>
    /// The shared-cluster address and the dedicated-silo addresses for one proxied service,
    /// keyed by the tenant domain each silo is provisioned for. Empty
    /// <paramref name="SiloAddressesByTenantDomain"/> is valid — every request then falls
    /// through to <paramref name="SharedAddress"/>.
    /// </summary>
    internal sealed record ServiceDestinations(string SharedAddress, IReadOnlyDictionary<string, string> SiloAddressesByTenantDomain);

    public static (IReadOnlyList<RouteConfig> Routes, IReadOnlyList<ClusterConfig> Clusters) Build(
        ServiceDestinations geoAssets, ServiceDestinations ipBilling, ServiceDestinations fieldService, ServiceDestinations neutralOperator)
    {
        ArgumentNullException.ThrowIfNull(geoAssets);
        ArgumentNullException.ThrowIfNull(ipBilling);
        ArgumentNullException.ThrowIfNull(fieldService);
        ArgumentNullException.ThrowIfNull(neutralOperator);

        var routes = new List<RouteConfig>();
        var clusters = new List<ClusterConfig>();

        AddClusters(clusters, "geoassets", geoAssets);
        AddClusters(clusters, "ip-billing", ipBilling);
        AddClusters(clusters, "field-service", fieldService);
        AddClusters(clusters, "neutral-operator", neutralOperator);

        AddRoutes(routes, "geoassets", "geoassets", $"/api/geoassets/{CatchAll}", geoAssets, firstOrder: 1, pathPattern: null);
        AddRoutes(routes, "ip-billing", "ip-billing", $"/api/ip-billing/{CatchAll}", ipBilling, firstOrder: 1, pathPattern: null);
        AddRoutes(routes, "field-service", "field-service", $"/api/field-service/{CatchAll}", fieldService, firstOrder: 1, pathPattern: null);
        AddRoutes(
            routes, "neutral-operator", "neutral-operator", $"{NeutralOperatorPathPrefix}/{CatchAll}", neutralOperator,
            firstOrder: 1, pathPattern: $"/api/{CatchAll}");

        AddRoutes(routes, "kernel-api", "geoassets", $"/api/{CatchAll}", geoAssets, KernelCatchAllFirstOrder, pathPattern: null);
        AddRoutes(routes, "kernel-wms", "geoassets", "/wms", geoAssets, KernelCatchAllFirstOrder, pathPattern: null);
        AddRoutes(routes, "kernel-wfs", "geoassets", "/wfs", geoAssets, KernelCatchAllFirstOrder, pathPattern: null);

        return (routes, clusters);
    }

    private static void AddClusters(List<ClusterConfig> clusters, string serviceName, ServiceDestinations destinations)
    {
        clusters.Add(SingleDestinationCluster(SharedClusterId(serviceName), destinations.SharedAddress));
        foreach (var (tenantDomain, siloAddress) in destinations.SiloAddressesByTenantDomain)
        {
            clusters.Add(SingleDestinationCluster(SiloClusterId(serviceName, tenantDomain), siloAddress));
        }
    }

    private static void AddRoutes(
        List<RouteConfig> routes,
        string routeName,
        string serviceName,
        string path,
        ServiceDestinations destinations,
        int firstOrder,
        string? pathPattern)
    {
        var transforms = Transforms(pathPattern);

        var order = firstOrder;
        foreach (var tenantDomain in destinations.SiloAddressesByTenantDomain.Keys)
        {
            routes.Add(new RouteConfig
            {
                RouteId = $"{routeName}-silo-route-{Slugify(tenantDomain)}",
                Order = order++,
                ClusterId = SiloClusterId(serviceName, tenantDomain),
                Match = new RouteMatch
                {
                    Path = path,
                    Headers = [new RouteHeader
                    {
                        Name = TenantDomainHeader,
                        Mode = HeaderMatchMode.ExactHeader,
                        Values = [tenantDomain],
                        IsCaseSensitive = false,
                    }],
                },
                Transforms = transforms,
            });
        }

        routes.Add(new RouteConfig
        {
            RouteId = $"{routeName}-shared-route",
            Order = order,
            ClusterId = SharedClusterId(serviceName),
            Match = new RouteMatch { Path = path },
            Transforms = transforms,
        });
    }

    private static List<IReadOnlyDictionary<string, string>> Transforms(string? pathPattern)
    {
        var transforms = new List<IReadOnlyDictionary<string, string>>();
        if (pathPattern is not null)
            transforms.Add(Transform(("PathPattern", pathPattern)));

        transforms.Add(Transform(("X-Forwarded", "Set")));
        transforms.AddRange(UntrustedOrganizationHeaders.Select(header => Transform(("RequestHeaderRemove", header))));
        transforms.AddRange(BackendCorsResponseHeaders.Select(header => Transform(("ResponseHeaderRemove", header), ("When", "Always"))));
        return transforms;
    }

    private static Dictionary<string, string> Transform(params (string Key, string Value)[] entries) =>
        entries.ToDictionary(e => e.Key, e => e.Value, StringComparer.OrdinalIgnoreCase);

    private static string SharedClusterId(string serviceName) => $"{serviceName}-shared";

    private static string SiloClusterId(string serviceName, string tenantDomain) => $"{serviceName}-silo-{Slugify(tenantDomain)}";

    private static ClusterConfig SingleDestinationCluster(string clusterId, string address) => new()
    {
        ClusterId = clusterId,
        Destinations = new Dictionary<string, DestinationConfig>(StringComparer.Ordinal)
        {
            ["destination1"] = new DestinationConfig { Address = address },
        },
    };

    /// <summary>A tenant domain turned into a valid, readable route/cluster id fragment — dots and anything non-alphanumeric become hyphens.</summary>
    private static string Slugify(string tenantDomain)
    {
        var chars = tenantDomain.ToUpperInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-').ToArray();
        return new string(chars);
    }
}
