using FluentAssertions;
using GeoAssets.NeutralOperator.Server.Tests.Support;
using GeoAssets.Server.Tests.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GeoAssets.NeutralOperator.Server.Tests.Routing;

/// <summary>
/// The routes of <c>GeoAssets.NeutralOperator.Server</c> (XD01-382), pinned in the kernel's route-inventory format — method, pattern and
/// authorization metadata — against <c>Snapshots/route-inventory.txt</c>. An intentional route change updates the snapshot in the same
/// PR (copy the <c>route-inventory.actual.txt</c> this test writes next to the test assembly over it) and justifies every changed line.
/// The host serves the chassis' service-order and target routes, the vertical's own (occupancy staging, shared-use dispatch) and the
/// two health endpoints — and nothing of the kernel's: no asset, identity, project, WMS/WFS or order-type-registry route.
/// </summary>
[Trait("Category", "RouteInventory")]
public sealed class RouteInventorySnapshotTests
{
    private const string SnapshotFile = "route-inventory.txt";

    private static string SnapshotDirectory => Path.Combine(AppContext.BaseDirectory, "Snapshots");

    private static async Task<IReadOnlyList<string>> ActualAsync()
    {
        await using var host = await NeutralOperatorHost.StartAsync();
        return RouteInventory.Describe(host.Services.GetRequiredService<EndpointDataSource>().Endpoints);
    }

    [Fact]
    public async Task NeutralOperatorServerRouteInventory_MatchesCheckedInSnapshot()
    {
        var actual = await ActualAsync();
        await File.WriteAllLinesAsync(Path.Combine(SnapshotDirectory, "route-inventory.actual.txt"), actual);
        var expected = (await File.ReadAllLinesAsync(Path.Combine(SnapshotDirectory, SnapshotFile))).Where(line => line.Length > 0).ToArray();

        var diff = RouteInventory.Compare(expected, actual);

        diff.IsEmpty.Should().BeTrue(
            because: "GeoAssets.NeutralOperator.Server's routes must match tests/GeoAssets.NeutralOperator.Server.Tests/Snapshots/{0}; if the change is intentional, update the snapshot in the same PR and justify each line. Diff (- snapshot, + actual):{1}{2}",
            SnapshotFile,
            Environment.NewLine,
            diff.Format());
    }

    [Fact]
    public async Task NeutralOperatorServerRoutes_AreTheChassisAndTheVerticalsOnly_NeverTheKernels()
    {
        var actual = await ActualAsync();

        actual.Should().NotContain(
            line => line.Contains("/api/geoassets/", StringComparison.Ordinal) || line.Contains("/api/identity/", StringComparison.Ordinal)
                || line.Contains("/api/projects", StringComparison.Ordinal) || line.Contains("/wms", StringComparison.Ordinal)
                || line.Contains("/wfs", StringComparison.Ordinal) || line.Contains("/api/registry/", StringComparison.Ordinal)
                || line.Contains("/api/targets/", StringComparison.Ordinal) || line.Contains("/selection/resolve", StringComparison.Ordinal),
            "the kernel's routes are reached over REST; map-gesture resolution is the kernel's /selection/resolve");
        actual.Should().Contain(["* /healthz | anonymous", "* /readyz | anonymous"]);
    }
}
