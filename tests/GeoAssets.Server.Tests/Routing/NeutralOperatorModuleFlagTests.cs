using FluentAssertions;
using GeoAssets.Hosting.Abstractions;
using GeoAssets.Identity.Authorization.Modules;
using GeoAssets.NeutralOperator.Application;
using GeoAssets.NeutralOperator.Application.Occupancy;
using GeoAssets.NeutralOperator.Domain.Occupancy;
using GeoAssets.NeutralOperator.Application.SharedUse;
using GeoAssets.NeutralOperator.Modules;
using GeoAssets.NeutralOperator.Persistence;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Registry;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GeoAssets.Server.Tests.Routing;

/// <summary>
/// The kernel's <c>GeoAssets:Modules</c> flag for the NeutralOperator vertical (XD01-382, AC6): with <c>neutral-operator</c> left out —
/// the cut-over to <c>GeoAssets.NeutralOperator.Server</c> — the real <c>Program</c> boots, exposes no NeutralOperator route (shared-use
/// dispatch, occupancy staging, GIS export, tenant portal) and registers nothing of the vertical (context, ports, order types,
/// descriptors, permissions, handlers); with it listed — the rollback of the cut-over (XD01-384), no longer the default — all of that is there.
/// </summary>
[Trait("Category", "RouteInventory")]
public sealed class NeutralOperatorModuleFlagTests
{
    private static readonly string[] NeutralOperatorRouteMarkers = ["/shared-use", "/occupancy", "/export/gis", "tenant-portal"];

    private static async Task<List<string>> RoutePatternsAsync(IServiceProvider services) =>
        [.. services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().Select(e => e.RoutePattern.RawText ?? string.Empty)];

    private static bool IsNeutralOperatorRoute(string pattern) =>
        NeutralOperatorRouteMarkers.Any(marker => pattern.Contains(marker, StringComparison.OrdinalIgnoreCase));

    [Fact]
    public async Task WithoutTheModule_TheKernelBoots_AndExposesNoNeutralOperatorRoute()
    {
        var services = await ServerHostFixture.GetNeutralOperatorDisabledServicesAsync();

        services.GetRequiredService<GeoAssetsModuleCatalog>().Modules.Select(m => m.Id).Should().NotContain(NeutralOperatorModule.ModuleId);
        var patterns = await RoutePatternsAsync(services);
        patterns.Where(IsNeutralOperatorRoute).Should().BeEmpty("shared-use, occupancy, GIS export and the tenant portal are the vertical's");
        patterns.Should().Contain("/api/workflow/service-orders", "the chassis' generic routes stay in the kernel");
    }

    [Fact]
    public async Task WithoutTheModule_TheKernelRegistersNothingOfTheVertical()
    {
        var services = await ServerHostFixture.GetNeutralOperatorDisabledServicesAsync();
        await using var scope = services.CreateAsyncScope();
        var provider = scope.ServiceProvider;

        provider.GetService<NeutralOperatorDbContext>().Should().BeNull();
        provider.GetService<IPoleOccupancyReader>().Should().BeNull();
        provider.GetService<IOrderOccupancyStagingService>().Should().BeNull();
        provider.GetService<ISharedUseDispatchService>().Should().BeNull();
        provider.GetService<IClandestineAttachmentService>().Should().BeNull();
        provider.GetServices<IOrderActionHandler>().Select(h => h.GetType().Namespace).Should().NotContain(
            ns => ns != null && ns.StartsWith("GeoAssets.NeutralOperator", StringComparison.Ordinal));
        provider.GetServices<IModulePermissionContributor>().Should().NotContain(c => string.Equals(c.VerticalId, NeutralOperatorModule.ModuleId, StringComparison.Ordinal));
        provider.GetRequiredService<IOrderTypeDescriptorRegistry>().GetAll().Should().NotContain(d => string.Equals(d.VerticalId, NeutralOperatorModule.ModuleId, StringComparison.Ordinal));
        var registry = provider.GetRequiredService<OrderTypeRegistry>();
        var present = new[] { SharedUseOrderTypes.PoleWorkId, SharedUseOrderTypes.OccupantNoticeId, ClandestineAttachmentOrderTypes.ClandestineNoticeId, TenantAttachmentRequestOrderTypes.AttachmentRequestId }
            .Where(id => registry.Find(id) is not null);
        present.Should().BeEmpty("the vertical's order types are registered by its module only");
    }

    [Fact]
    public async Task WithTheModuleListed_TheRollbackConfiguration_TheKernelServesTheVerticalAsBefore()
    {
        var services = await ServerHostFixture.GetNeutralOperatorEnabledServicesAsync();
        await using var scope = services.CreateAsyncScope();
        var provider = scope.ServiceProvider;

        services.GetRequiredService<GeoAssetsModuleCatalog>().Modules.Select(m => m.Id).Should().Contain(NeutralOperatorModule.ModuleId);
        var routes = (await RoutePatternsAsync(services)).Where(IsNeutralOperatorRoute).ToList();
        routes.Should().Contain("/api/geoassets/export/gis/v1")
            .And.Contain("/api/workflow/service-orders/{id}/shared-use/dispatch")
            .And.Contain("/api/workflow/service-orders/{id}/occupancy-changes");
        provider.GetRequiredService<NeutralOperatorDbContext>().Should().NotBeNull();
        provider.GetRequiredService<ISharedUseDispatchService>().Should().NotBeNull();
        provider.GetServices<IModulePermissionContributor>().Should().Contain(c => string.Equals(c.VerticalId, NeutralOperatorModule.ModuleId, StringComparison.Ordinal));
        provider.GetRequiredService<OrderTypeRegistry>().Find(SharedUseOrderTypes.PoleWorkId).Should().NotBeNull();
    }

    [Fact]
    public async Task TheKernelHostedModule_IsWhatProgramComposes_NotTheLibraryModuleOnItsOwn()
    {
        var services = await ServerHostFixture.GetNeutralOperatorEnabledServicesAsync();

        var module = services.GetRequiredService<GeoAssetsModuleCatalog>().Modules.Single(m => string.Equals(m.Id, NeutralOperatorModule.ModuleId, StringComparison.Ordinal));

        module.Should().BeOfType<GeoAssets.Server.Modules.NeutralOperator.KernelHostedNeutralOperatorModule>(
            "the GIS export and the in-process asset-mutation apply exist only where the host serves its own asset catalog");
    }
}
