using Yarp.ReverseProxy.Configuration;

namespace GeoAssets.Gateway;

/// <summary>
/// Builds the gateway's route/cluster table (XD01-213, per-tenant silo selection XD01-289): for
/// each proxied service, one dedicated-silo route per provisioned tenant domain (<see cref="TenantDomainHeader"/>
/// matched against that domain's exact value, case-insensitively, each at a lower <c>Order</c>
/// than the shared route — lower runs first in YARP's precedence) and a shared multi-tenant
/// route matching the identical path pattern with no header condition, so a silo tenant's
/// request is routed to its own cluster even though all routes match the same path, while a
/// request carrying no domain, or a domain with no provisioned silo, falls through to the shared
/// cluster rather than an arbitrary/wrong tenant's silo.
/// </summary>
internal static class GatewayRouteCatalog
{
    public const string TenantDomainHeader = "X-Tenant-Domain";

    /// <summary>
    /// The shared-cluster address and the dedicated-silo addresses for one proxied service,
    /// keyed by the tenant domain each silo is provisioned for. Empty
    /// <paramref name="SiloAddressesByTenantDomain"/> is valid — every request then falls
    /// through to <paramref name="SharedAddress"/>.
    /// </summary>
    internal sealed record ServiceDestinations(string SharedAddress, IReadOnlyDictionary<string, string> SiloAddressesByTenantDomain);

    public static (IReadOnlyList<RouteConfig> Routes, IReadOnlyList<ClusterConfig> Clusters) Build(
        ServiceDestinations geoAssets, ServiceDestinations ipBilling, ServiceDestinations fieldService)
    {
        var routes = new List<RouteConfig>();
        var clusters = new List<ClusterConfig>();

        AddService(routes, clusters, "geoassets", "/api/geoassets/{**catch-all}", geoAssets);
        AddService(routes, clusters, "ip-billing", "/api/ip-billing/{**catch-all}", ipBilling);
        AddService(routes, clusters, "field-service", "/api/field-service/{**catch-all}", fieldService);

        return (routes, clusters);
    }

    private static void AddService(
        List<RouteConfig> routes, List<ClusterConfig> clusters, string serviceName, string pathPattern, ServiceDestinations destinations)
    {
        var sharedClusterId = $"{serviceName}-shared";
        clusters.Add(SingleDestinationCluster(sharedClusterId, destinations.SharedAddress));

        var order = 1;
        foreach (var (tenantDomain, siloAddress) in destinations.SiloAddressesByTenantDomain)
        {
            var slug = Slugify(tenantDomain);
            var siloClusterId = $"{serviceName}-silo-{slug}";
            clusters.Add(SingleDestinationCluster(siloClusterId, siloAddress));

            routes.Add(new RouteConfig
            {
                RouteId = $"{serviceName}-silo-route-{slug}",
                Order = order++,
                ClusterId = siloClusterId,
                Match = new RouteMatch
                {
                    Path = pathPattern,
                    Headers = [new RouteHeader
                    {
                        Name = TenantDomainHeader,
                        Mode = HeaderMatchMode.ExactHeader,
                        Values = [tenantDomain],
                        IsCaseSensitive = false,
                    }],
                },
            });
        }

        routes.Add(new RouteConfig
        {
            RouteId = $"{serviceName}-shared-route",
            Order = order,
            ClusterId = sharedClusterId,
            Match = new RouteMatch { Path = pathPattern },
        });
    }

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
