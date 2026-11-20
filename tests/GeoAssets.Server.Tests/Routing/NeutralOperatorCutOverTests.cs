using System.Text.Json;
using FluentAssertions;
using GeoAssets.Hosting.Abstractions;
using GeoAssets.NeutralOperator.Modules;
using GeoAssets.NeutralOperator.Persistence;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Registry;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GeoAssets.Server.Tests.Routing;

/// <summary>
/// XD01-384 (AC: "Kernel route-inventory snapshot: NeutralOperator routes absent"): the cut-over's default. The kernel as
/// <c>appsettings.json</c> composes it hosts none of the NeutralOperator module's eleven routes, registers none of its
/// services, and lists no module but postgres-spatial; the host that lists the module again (the rollback)
/// serves exactly those eleven routes more, and nothing else differs.
/// </summary>
[Trait("Category", "RouteInventory")]
public sealed class NeutralOperatorCutOverTests
{
    /// <summary>The routes the kernel lost when the cut-over left the module out of the default composition.</summary>
    private static readonly string[] NeutralOperatorRoutes =
    [
        "DELETE /api/workflow/service-orders/{id}/occupancy-changes | none",
        "DELETE /api/workflow/service-orders/{id}/targets/{targetId}/occupancy-changes | none",
        "GET /api/geoassets/export/gis/v1 | policy:features:read",
        "GET /api/workflow/service-orders/{id}/occupancy-changes | policy:serviceorders:read",
        "GET /api/workflow/service-orders/{id}/shared-use/dispatch-plan | policy:serviceorders:read",
        "GET /api/workflow/service-orders/{id}/shared-use/dispatch-plan/skipped-entries | policy:serviceorders:read",
        "GET /api/workflow/service-orders/{id}/shared-use/dispatch-plan/unnotifiable-poles | policy:serviceorders:read",
        "GET /api/workflow/service-orders/{id}/targets/{targetId}/occupancy | policy:serviceorders:read",
        "POST /api/workflow/service-orders/{id}/occupancy-saga/retry | none",
        "POST /api/workflow/service-orders/{id}/shared-use/dispatch | none",
        "PUT /api/workflow/service-orders/{id}/targets/{targetId}/occupancy-changes | none",
    ];

    private static string[] Routes(IServiceProvider services) =>
        [.. RouteInventory.Describe(services.GetRequiredService<EndpointDataSource>().Endpoints)];

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GeoAssets.sln")))
            directory = directory.Parent;

        directory.Should().NotBeNull("the solution root is above the test output");
        return directory!.FullName;
    }

    [Fact]
    public void TheCutOverRemovesElevenRoutes() => NeutralOperatorRoutes.Should().HaveCount(11).And.OnlyHaveUniqueItems();

    [Fact]
    public async Task TheDefaultHost_HostsNoneOfTheNeutralOperatorRoutes()
    {
        var actual = Routes(await ServerHostFixture.GetServicesAsync());

        actual.Intersect(NeutralOperatorRoutes, StringComparer.Ordinal).Should().BeEmpty();
        actual.Should().NotContain(line => line.Contains("/shared-use", StringComparison.Ordinal) || line.Contains("/occupancy", StringComparison.Ordinal)
            || line.Contains("/export/gis", StringComparison.Ordinal) || line.Contains("tenant-portal", StringComparison.Ordinal),
            "shared-use, occupancy, saga retry, GIS export and the tenant portal are the vertical's");
    }

    [Fact]
    public async Task TheCheckedInSnapshot_HoldsNoneOfTheNeutralOperatorRoutes()
    {
        var snapshot = (await File.ReadAllLinesAsync(Path.Combine(AppContext.BaseDirectory, "Snapshots", "route-inventory.txt"))).Where(line => line.Length > 0).ToArray();

        snapshot.Intersect(NeutralOperatorRoutes, StringComparer.Ordinal).Should().BeEmpty();
        snapshot.Should().Contain("GET /api/workflow/service-orders/{id}/targets | policy:serviceorders:read", "the chassis' generic routes stay in the kernel");
    }

    [Fact]
    public async Task TheDefaultHost_MatchesTheCheckedInSnapshot()
    {
        var snapshot = (await File.ReadAllLinesAsync(Path.Combine(AppContext.BaseDirectory, "Snapshots", "route-inventory.txt"))).Where(line => line.Length > 0).ToArray();

        var diff = RouteInventory.Compare(snapshot, Routes(await ServerHostFixture.GetServicesAsync()));

        diff.Added.Should().BeEmpty();
        diff.Removed.Should().BeEmpty();
    }

    [Fact]
    public async Task TheHostThatListsTheModule_RestoresExactlyTheElevenRoutes_AndNothingElse()
    {
        var withoutModule = Routes(await ServerHostFixture.GetServicesAsync());
        var withModule = Routes(await ServerHostFixture.GetNeutralOperatorEnabledServicesAsync());

        var diff = RouteInventory.Compare(withoutModule, withModule);

        diff.Removed.Should().BeEmpty("listing the module again adds routes, it removes none");
        diff.Added.Should().BeEquivalentTo(NeutralOperatorRoutes);
    }

    [Fact]
    public async Task TheDefaultHost_ListsOnlyPostgresSpatial_AndRegistersNothingOfTheVertical()
    {
        var services = await ServerHostFixture.GetServicesAsync();
        await using var scope = services.CreateAsyncScope();
        var provider = scope.ServiceProvider;

        services.GetRequiredService<GeoAssetsModuleCatalog>().Modules.Select(m => m.Id).Should().Equal("postgres-spatial");
        provider.GetService<NeutralOperatorDbContext>().Should().BeNull();
        provider.GetRequiredService<IOrderTypeDescriptorRegistry>().GetAll().Should().NotContain(d => string.Equals(d.VerticalId, NeutralOperatorModule.ModuleId, StringComparison.Ordinal));
        provider.GetRequiredService<OrderTypeRegistry>().Find(GeoAssets.NeutralOperator.Application.SharedUse.SharedUseOrderTypes.PoleWorkId).Should().BeNull();
    }

    [Fact]
    public async Task TheHostThatListsTheModule_RegistersTheVerticalsContext_AndPublishesItsDescriptorsOnTheGatewayRoute()
    {
        var services = await ServerHostFixture.GetNeutralOperatorEnabledServicesAsync();
        await using var scope = services.CreateAsyncScope();

        scope.ServiceProvider.GetRequiredService<NeutralOperatorDbContext>().Should().NotBeNull();
        var descriptors = services.GetRequiredService<IOrderTypeDescriptorRegistry>().GetAll()
            .Where(d => string.Equals(d.VerticalId, NeutralOperatorModule.ModuleId, StringComparison.Ordinal)).ToList();
        descriptors.Should().NotBeEmpty();
        descriptors.Should().OnlyContain(d => string.Equals(d.ApiBaseRoute, "/api/v/neutral-operator/workflow/service-orders", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheServersAppSettings_ListOnlyPostgresSpatialByDefault_AndNoNeutralOperator()
    {
        var path = Path.Combine(RepositoryRoot(), "apps", "GeoAssets.Server", "appsettings.json");
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path));

        var modules = document.RootElement.GetProperty("GeoAssets").GetProperty("Modules").EnumerateArray().Select(m => m.GetString()).ToList();

        modules.Should().Equal("postgres-spatial");
    }
}
