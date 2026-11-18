using System.Text.RegularExpressions;
using FluentAssertions;
using GeoAssets.Hosting.Abstractions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GeoAssets.Server.Tests.Routing;

/// <summary>
/// Acceptance for XD01-358: with every module disabled (<c>GeoAssets__Modules__0=</c>) the real <c>Program</c> still
/// boots, and the only routes it loses against the checked-in inventory are the neutral-operator module's.
/// </summary>
[Trait("Category", "RouteInventory")]
public sealed class KernelOnlyBootTests
{
    private static readonly Regex NeutralOperatorRoute = new(
        @"/api/isp/tenant-portal(/|\s|$)|/occupancy|/shared-use(/|\s|$)",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        TimeSpan.FromSeconds(1));

    [Fact]
    public async Task Server_WithNeutralOperatorModuleDisabled_BootsWithoutAnyOfItsRoutes()
    {
        var services = await ServerHostFixture.GetKernelOnlyServicesAsync();
        var actual = RouteInventory.Describe(services.GetRequiredService<EndpointDataSource>().Endpoints);
        var expected = (await File.ReadAllLinesAsync(Path.Combine(AppContext.BaseDirectory, "Snapshots", "route-inventory.txt")))
            .Where(line => line.Length > 0)
            .ToArray();

        var diff = RouteInventory.Compare(expected, actual);
        var removed = string.Join(Environment.NewLine, diff.Removed);

        services.GetRequiredService<GeoAssetsModuleCatalog>().Modules.Should().BeEmpty();
        diff.Added.Should().BeEmpty("disabling a module must not add routes. Added:{0}{1}", Environment.NewLine, string.Join(Environment.NewLine, diff.Added));
        diff.Removed.Should().NotBeEmpty("the neutral-operator module contributes routes to the snapshot");
        diff.Removed.Should().OnlyContain(
            line => NeutralOperatorRoute.IsMatch(line),
            "only neutral-operator routes may disappear. Removed:{0}{1}",
            Environment.NewLine,
            removed);
        expected.Where(line => NeutralOperatorRoute.IsMatch(line)).Should().BeSubsetOf(
            diff.Removed,
            "every neutral-operator route must be gone. Removed:{0}{1}",
            Environment.NewLine,
            removed);
    }
}
