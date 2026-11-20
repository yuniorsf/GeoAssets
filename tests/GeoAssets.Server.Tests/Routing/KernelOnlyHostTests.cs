using System.Net;
using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Hosting.Abstractions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GeoAssets.Server.Tests.Routing;

/// <summary>
/// Acceptance for XD01-359 (invariant I5): with every module entry blank the real <c>Program</c> still boots on a plain
/// in-memory <c>IAssetProvider</c>, serves the kernel's endpoints, and the only routes it loses against the checked-in
/// inventory are the enabled modules' — the IP-billing grant bridge's and the PostGIS WMS routes (neutral-operator's are not in the
/// inventory since the XD01-384 cut-over).
/// </summary>
[Trait("Category", "RouteInventory")]
public sealed class KernelOnlyHostTests
{
    private static readonly string[] ExpectedRemoved =
    [
        "GET /api/geoassets/wms | anonymous",
        "GET /wms | anonymous",
    ];

    [Fact]
    public async Task KernelOnly_HostStarts_AndServesHealthFeaturesAndOrderTypes()
    {
        var factory = await ServerHostFixture.GetKernelOnlyFactoryAsync();
        using var client = factory.CreateClient();

        factory.Services.GetRequiredService<GeoAssetsModuleCatalog>().Modules.Should().BeEmpty();
        (await client.GetAsync(new Uri("/healthz", UriKind.Relative))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync(new Uri("/api/geoassets/features", UriKind.Relative))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync(new Uri("/api/workflow/order-types", UriKind.Relative))).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task KernelOnly_CatalogPool_SkipsTheUnregisteredPostgresCatalog()
    {
        var factory = await ServerHostFixture.GetKernelOnlyFactoryAsync();
        using var client = factory.CreateClient();

        factory.Services.GetRequiredService<ICatalogPool>().CatalogIds.Should().BeEmpty();
        (await client.GetAsync(new Uri("/healthz", UriKind.Relative))).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task KernelOnly_RouteInventory_IsTheSnapshotMinusEveryModulesRoutes()
    {
        var factory = await ServerHostFixture.GetKernelOnlyFactoryAsync();
        var actual = RouteInventory.Describe(factory.Services.GetRequiredService<EndpointDataSource>().Endpoints);
        var expected = (await File.ReadAllLinesAsync(Path.Combine(AppContext.BaseDirectory, "Snapshots", "route-inventory.txt")))
            .Where(line => line.Length > 0)
            .ToArray();

        var diff = RouteInventory.Compare(expected, actual);

        diff.Added.Should().BeEmpty("a kernel-only host must not add routes. Added:{0}{1}", Environment.NewLine, string.Join(Environment.NewLine, diff.Added));
        diff.Removed.Should().BeEquivalentTo(
            ExpectedRemoved,
            "exactly the modules' routes may disappear. Removed:{0}{1}",
            Environment.NewLine,
            string.Join(Environment.NewLine, diff.Removed));
    }
}
