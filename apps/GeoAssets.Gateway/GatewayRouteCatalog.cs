using Yarp.ReverseProxy.Configuration;

namespace GeoAssets.Gateway;

/// <summary>
/// Builds the gateway's route/cluster table (XD01-213): for each proxied service, a
/// dedicated-silo route (<see cref="TenantDomainHeader"/> present, <c>Order = 1</c> — lower
/// runs first in YARP's precedence) and a shared multi-tenant route matching the identical path
/// pattern with no header condition (<c>Order = 2</c>), so a silo tenant's request is routed to
/// its own cluster even though both routes match the same path, and every other request falls
/// through to the shared cluster.
/// </summary>
internal static class GatewayRouteCatalog
{
    public const string TenantDomainHeader = "X-Tenant-Domain";

    /// <summary>The shared-cluster and dedicated-silo-cluster base addresses for one proxied service.</summary>
    internal sealed record ServiceDestinations(string SharedAddress, string SiloAddress);

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
        var siloClusterId = $"{serviceName}-silo";
        var sharedClusterId = $"{serviceName}-shared";

        clusters.Add(SingleDestinationCluster(siloClusterId, destinations.SiloAddress));
        clusters.Add(SingleDestinationCluster(sharedClusterId, destinations.SharedAddress));

        routes.Add(new RouteConfig
        {
            RouteId = $"{serviceName}-silo-route",
            Order = 1,
            ClusterId = siloClusterId,
            Match = new RouteMatch
            {
                Path = pathPattern,
                Headers = [new RouteHeader { Name = TenantDomainHeader, Mode = HeaderMatchMode.Exists }],
            },
        });

        routes.Add(new RouteConfig
        {
            RouteId = $"{serviceName}-shared-route",
            Order = 2,
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
}
