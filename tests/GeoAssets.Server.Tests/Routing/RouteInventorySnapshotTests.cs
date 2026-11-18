using FluentAssertions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GeoAssets.Server.Tests.Routing;

/// <summary>
/// Behaviour-parity guard for the server-decoupling programme (XD01-354, ADR-001 Phase 0): the real host's routes —
/// method, pattern and authorization metadata — must match <c>Snapshots/route-inventory.txt</c>. An intentional route
/// change updates the snapshot in the same PR (copy the <c>route-inventory.actual.txt</c> this test writes next to the
/// test assembly over the snapshot) and justifies every changed line in the PR body.
/// </summary>
[Trait("Category", "RouteInventory")]
public sealed class RouteInventorySnapshotTests
{
    private const string SnapshotFile = "route-inventory.txt";

    [Fact]
    public async Task ServerRouteInventory_MatchesCheckedInSnapshot()
    {
        var services = await ServerHostFixture.GetServicesAsync();
        var actual = RouteInventory.Describe(services.GetRequiredService<EndpointDataSource>().Endpoints);

        var snapshotDirectory = Path.Combine(AppContext.BaseDirectory, "Snapshots");
        await File.WriteAllLinesAsync(Path.Combine(snapshotDirectory, "route-inventory.actual.txt"), actual);
        var expected = (await File.ReadAllLinesAsync(Path.Combine(snapshotDirectory, SnapshotFile)))
            .Where(line => line.Length > 0)
            .ToArray();

        var diff = RouteInventory.Compare(expected, actual);

        diff.IsEmpty.Should().BeTrue(
            because: "GeoAssets.Server's routes must match tests/GeoAssets.Server.Tests/Snapshots/{0} (XD01-354); if the change is intentional, update the snapshot in the same PR and justify each line. Diff (- snapshot, + actual):{1}{2}",
            SnapshotFile,
            Environment.NewLine,
            diff.Format());
    }
}
