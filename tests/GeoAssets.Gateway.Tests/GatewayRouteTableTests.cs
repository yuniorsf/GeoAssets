using FluentAssertions;
using Xunit;
using Yarp.ReverseProxy.Configuration;

namespace GeoAssets.Gateway.Tests;

/// <summary>
/// XD01-384 (AC: "gateway route table test present"): the gateway's whole route/cluster table, pinned — ids, orders, paths,
/// header conditions, clusters and transforms — with and without dedicated silos for each service. A change to the table
/// (a new route, a moved order, a dropped transform) fails here and has to be made on purpose.
/// </summary>
public sealed class GatewayRouteTableTests
{
    private static readonly Dictionary<string, string> _noSilos = new(StringComparer.OrdinalIgnoreCase);

    private static GatewayRouteCatalog.ServiceDestinations Shared(string address) => new(address, _noSilos);

    private static GatewayRouteCatalog.ServiceDestinations WithSilos(string address, params (string Domain, string Address)[] silos) =>
        new(address, silos.ToDictionary(s => s.Domain, s => s.Address, StringComparer.OrdinalIgnoreCase));

    private static readonly string[] _transforms =
    [
        "X-Forwarded=Set",
        "RequestHeaderRemove=X-GeoAssets-Organization-Id",
        "RequestHeaderRemove=X-Organization-Id",
        "ResponseHeaderRemove=Access-Control-Allow-Origin;When=Always",
        "ResponseHeaderRemove=Access-Control-Allow-Credentials;When=Always",
        "ResponseHeaderRemove=Access-Control-Allow-Headers;When=Always",
        "ResponseHeaderRemove=Access-Control-Allow-Methods;When=Always",
        "ResponseHeaderRemove=Access-Control-Expose-Headers;When=Always",
        "ResponseHeaderRemove=Access-Control-Max-Age;When=Always",
    ];

    private static string Describe(RouteConfig route) =>
        string.Join(
            " | ",
            [
                route.RouteId!,
                $"order={route.Order}",
                $"path={route.Match.Path}",
                $"cluster={route.ClusterId}",
                $"headers={string.Join(',', (route.Match.Headers ?? []).Select(h => $"{h.Name}:{h.Mode}:{string.Join('/', h.Values ?? [])}:cs={h.IsCaseSensitive}"))}",
                $"transforms={string.Join(" , ", route.Transforms!.Select(t => string.Join(';', t.OrderBy(kv => string.Equals(kv.Key, "When", StringComparison.Ordinal) ? 1 : 0).ThenBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{kv.Key}={kv.Value}"))))}",
            ]);

    private static string Describe(ClusterConfig cluster) =>
        $"{cluster.ClusterId} -> {string.Join(',', cluster.Destinations!.Select(d => $"{d.Key}={d.Value.Address}"))}";

    private static string Transforms(params string[] leading) => string.Join(" , ", [.. leading, .. _transforms]);

    private static string Line(string id, int order, string path, string cluster, string headers, string transforms) =>
        $"{id} | order={order} | path={path} | cluster={cluster} | headers={headers} | transforms={transforms}";

    private const string SiloHeader = "X-Tenant-Domain:ExactHeader:";

    [Fact]
    public void WithoutSilos_TheTableIsTheSevenSharedRoutes_AndTheFourSharedClusters()
    {
        var (routes, clusters) = GatewayRouteCatalog.Build(Shared("http://k"), Shared("http://ip"), Shared("http://fs"), Shared("http://no"));

        routes.Select(Describe).Should().Equal(
            Line("geoassets-shared-route", 1, "/api/geoassets/{**catch-all}", "geoassets-shared", "", Transforms()),
            Line("ip-billing-shared-route", 1, "/api/ip-billing/{**catch-all}", "ip-billing-shared", "", Transforms()),
            Line("field-service-shared-route", 1, "/api/field-service/{**catch-all}", "field-service-shared", "", Transforms()),
            Line("neutral-operator-shared-route", 1, "/api/v/neutral-operator/{**catch-all}", "neutral-operator-shared", "", Transforms("PathPattern=/api/{**catch-all}")),
            Line("kernel-api-shared-route", 10000, "/api/{**catch-all}", "geoassets-shared", "", Transforms()),
            Line("kernel-wms-shared-route", 10000, "/wms", "geoassets-shared", "", Transforms()),
            Line("kernel-wfs-shared-route", 10000, "/wfs", "geoassets-shared", "", Transforms()));
        clusters.Select(Describe).Should().Equal(
            "geoassets-shared -> destination1=http://k",
            "ip-billing-shared -> destination1=http://ip",
            "field-service-shared -> destination1=http://fs",
            "neutral-operator-shared -> destination1=http://no");
    }

    [Fact]
    public void WithASiloForEveryService_EachServiceGainsASiloRouteBelowItsSharedRoute_AndTheKernelCatchAllsFollowTheKernelsSilos()
    {
        var (routes, clusters) = GatewayRouteCatalog.Build(
            WithSilos("http://k", ("acme.example.com", "http://k-acme")),
            WithSilos("http://ip", ("acme.example.com", "http://ip-acme")),
            WithSilos("http://fs", ("acme.example.com", "http://fs-acme")),
            WithSilos("http://no", ("acme.example.com", "http://no-acme")));

        routes.Select(Describe).Should().Equal(
            Line("geoassets-silo-route-ACME-EXAMPLE-COM", 1, "/api/geoassets/{**catch-all}", "geoassets-silo-ACME-EXAMPLE-COM", $"{SiloHeader}acme.example.com:cs=False", Transforms()),
            Line("geoassets-shared-route", 2, "/api/geoassets/{**catch-all}", "geoassets-shared", "", Transforms()),
            Line("ip-billing-silo-route-ACME-EXAMPLE-COM", 1, "/api/ip-billing/{**catch-all}", "ip-billing-silo-ACME-EXAMPLE-COM", $"{SiloHeader}acme.example.com:cs=False", Transforms()),
            Line("ip-billing-shared-route", 2, "/api/ip-billing/{**catch-all}", "ip-billing-shared", "", Transforms()),
            Line("field-service-silo-route-ACME-EXAMPLE-COM", 1, "/api/field-service/{**catch-all}", "field-service-silo-ACME-EXAMPLE-COM", $"{SiloHeader}acme.example.com:cs=False", Transforms()),
            Line("field-service-shared-route", 2, "/api/field-service/{**catch-all}", "field-service-shared", "", Transforms()),
            Line("neutral-operator-silo-route-ACME-EXAMPLE-COM", 1, "/api/v/neutral-operator/{**catch-all}", "neutral-operator-silo-ACME-EXAMPLE-COM", $"{SiloHeader}acme.example.com:cs=False", Transforms("PathPattern=/api/{**catch-all}")),
            Line("neutral-operator-shared-route", 2, "/api/v/neutral-operator/{**catch-all}", "neutral-operator-shared", "", Transforms("PathPattern=/api/{**catch-all}")),
            Line("kernel-api-silo-route-ACME-EXAMPLE-COM", 10000, "/api/{**catch-all}", "geoassets-silo-ACME-EXAMPLE-COM", $"{SiloHeader}acme.example.com:cs=False", Transforms()),
            Line("kernel-api-shared-route", 10001, "/api/{**catch-all}", "geoassets-shared", "", Transforms()),
            Line("kernel-wms-silo-route-ACME-EXAMPLE-COM", 10000, "/wms", "geoassets-silo-ACME-EXAMPLE-COM", $"{SiloHeader}acme.example.com:cs=False", Transforms()),
            Line("kernel-wms-shared-route", 10001, "/wms", "geoassets-shared", "", Transforms()),
            Line("kernel-wfs-silo-route-ACME-EXAMPLE-COM", 10000, "/wfs", "geoassets-silo-ACME-EXAMPLE-COM", $"{SiloHeader}acme.example.com:cs=False", Transforms()),
            Line("kernel-wfs-shared-route", 10001, "/wfs", "geoassets-shared", "", Transforms()));
        clusters.Select(Describe).Should().Equal(
            "geoassets-shared -> destination1=http://k",
            "geoassets-silo-ACME-EXAMPLE-COM -> destination1=http://k-acme",
            "ip-billing-shared -> destination1=http://ip",
            "ip-billing-silo-ACME-EXAMPLE-COM -> destination1=http://ip-acme",
            "field-service-shared -> destination1=http://fs",
            "field-service-silo-ACME-EXAMPLE-COM -> destination1=http://fs-acme",
            "neutral-operator-shared -> destination1=http://no",
            "neutral-operator-silo-ACME-EXAMPLE-COM -> destination1=http://no-acme");
    }

    [Theory]
    [InlineData("geoassets", 2)]
    [InlineData("ip-billing", 1)]
    [InlineData("field-service", 1)]
    [InlineData("neutral-operator", 3)]
    public void SilosOfOneServiceOnly_AddRoutesToThatServicesPrefixOnly_InTheirDeclarationOrder_BeforeTheSharedRoute(string service, int silos)
    {
        var domains = Enumerable.Range(1, silos).Select(i => ($"t{i}.example.com", $"http://silo-{i}")).ToArray();
        GatewayRouteCatalog.ServiceDestinations For(string name) => string.Equals(name, service, StringComparison.Ordinal) ? WithSilos("http://shared", domains) : Shared("http://shared");

        var (routes, _) = GatewayRouteCatalog.Build(For("geoassets"), For("ip-billing"), For("field-service"), For("neutral-operator"));

        var own = routes.Where(r => r.RouteId!.StartsWith($"{service}-", StringComparison.Ordinal)).ToList();
        own.Select(r => r.Order).Should().Equal(Enumerable.Range(1, silos + 1).Select(i => (int?)i));
        own.Select(r => r.RouteId).Last().Should().Be($"{service}-shared-route");
        own.Take(silos).Select(r => r.RouteId).Should().Equal(Enumerable.Range(1, silos).Select(i => $"{service}-silo-route-T{i}-EXAMPLE-COM"));
        routes.Where(r => !r.RouteId!.StartsWith($"{service}-", StringComparison.Ordinal) && !r.RouteId!.StartsWith("kernel-", StringComparison.Ordinal))
            .Should().OnlyContain(r => r.Order == 1, "the other services have no silo");
    }

    [Fact]
    public void EveryRoute_CarriesTheSameSecurityTransforms_AndNoRouteForwardsAnAuthorizationChange()
    {
        var (routes, _) = GatewayRouteCatalog.Build(
            WithSilos("http://k", ("a.example.com", "http://ka")), Shared("http://ip"), Shared("http://fs"), WithSilos("http://no", ("a.example.com", "http://na")));

        foreach (var route in routes)
        {
            var transforms = route.Transforms!.Select(t => string.Join(';', t.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{kv.Key}={kv.Value}"))).ToList();
            transforms.Should().Contain("X-Forwarded=Set", route.RouteId);
            transforms.Should().Contain("RequestHeaderRemove=X-GeoAssets-Organization-Id", route.RouteId);
            transforms.Should().Contain("RequestHeaderRemove=X-Organization-Id", route.RouteId);
            transforms.Count(t => t.StartsWith("ResponseHeaderRemove=Access-Control-", StringComparison.Ordinal)).Should().Be(6, route.RouteId);
            transforms.Should().NotContain(t => t.Contains("Authorization", StringComparison.OrdinalIgnoreCase), "the token is forwarded untouched: {0}", route.RouteId);
            route.AuthorizationPolicy.Should().BeNull("the gateway authorizes nothing: {0}", route.RouteId);
        }
    }

    [Fact]
    public void OnlyTheNeutralOperatorRoutes_RewriteThePath_ToTheApiPrefix()
    {
        var (routes, _) = GatewayRouteCatalog.Build(Shared("http://k"), Shared("http://ip"), Shared("http://fs"), Shared("http://no"));

        routes.Where(r => r.Transforms!.Any(t => t.ContainsKey("PathPattern"))).Select(r => r.RouteId)
            .Should().Equal("neutral-operator-shared-route");
        routes.Single(r => string.Equals(r.RouteId, "neutral-operator-shared-route", StringComparison.Ordinal)).Transforms![0]["PathPattern"].Should().Be("/api/{**catch-all}");
    }

    [Fact]
    public void TheKernelCatchAlls_AreTheOnlyRoutesAtOrAboveTheCatchAllOrder_AndEveryOtherRouteWinsOverThem()
    {
        var (routes, _) = GatewayRouteCatalog.Build(
            WithSilos("http://k", ("a.example.com", "http://ka"), ("b.example.com", "http://kb")),
            WithSilos("http://ip", ("a.example.com", "http://ipa")), Shared("http://fs"), WithSilos("http://no", ("a.example.com", "http://na"), ("b.example.com", "http://nb")));

        var catchAll = routes.Where(r => r.Order >= GatewayRouteCatalog.KernelCatchAllFirstOrder).ToList();
        catchAll.Select(r => r.RouteId).Should().OnlyContain(id => id!.StartsWith("kernel-", StringComparison.Ordinal));
        routes.Where(r => r.Order < GatewayRouteCatalog.KernelCatchAllFirstOrder).Max(r => r.Order).Should().BeLessThan(GatewayRouteCatalog.KernelCatchAllFirstOrder);
        GatewayRouteCatalog.KernelCatchAllFirstOrder.Should().Be(10_000);
    }

    [Fact]
    public void RouteIdsAndClusterIds_AreUnique_AndEveryRouteTargetsAnExistingCluster()
    {
        var (routes, clusters) = GatewayRouteCatalog.Build(
            WithSilos("http://k", ("a.example.com", "http://ka")), WithSilos("http://ip", ("a.example.com", "http://ipa")),
            WithSilos("http://fs", ("a.example.com", "http://fsa")), WithSilos("http://no", ("a.example.com", "http://na")));

        routes.Select(r => r.RouteId).Should().OnlyHaveUniqueItems();
        clusters.Select(c => c.ClusterId).Should().OnlyHaveUniqueItems();
        routes.Select(r => r.ClusterId).Should().BeSubsetOf(clusters.Select(c => c.ClusterId));
    }

    [Fact]
    public void TheTenantDomainHeader_IsXTenantDomain() => GatewayRouteCatalog.TenantDomainHeader.Should().Be("X-Tenant-Domain");

    [Fact]
    public void ThePublicPrefixOfTheVertical_IsApiVNeutralOperator() => GatewayRouteCatalog.NeutralOperatorPathPrefix.Should().Be("/api/v/neutral-operator");

    [Fact]
    public void Build_WithANullService_IsRejected()
    {
        var act = () => GatewayRouteCatalog.Build(Shared("http://k"), Shared("http://ip"), Shared("http://fs"), null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
