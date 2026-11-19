using FluentAssertions;
using GeoAssets.Server.Modules.NeutralOperator;
using GeoAssets.Workflow.Isp.NeutralOperator.SharedUse;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GeoAssets.Server.Tests;

/// <summary>
/// This host's wiring of the shared-use dispatch (XD01-316): the order types it registers and
/// the dispatch service with its unit of work and telemetry. The outbox and broker wiring is
/// covered by <c>WorkflowMessagingHostExtensionsTests</c> (XD01-357).
/// </summary>
public sealed class SharedUseHostExtensionsTests
{
    private static IConfiguration Configuration(params (string Key, string? Value)[] settings) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build();

    // ── Order types ───────────────────────────────────────────────────────────

    [Fact]
    public void RegisterSharedUseOrderTypes_TargetsTheOccupiableSupportRole_NotAnyAssetTypeId()
    {
        var registry = new OrderTypeRegistry();

        registry.RegisterSharedUseOrderTypes();

        foreach (var id in new[] { SharedUseOrderTypes.PoleWorkId, SharedUseOrderTypes.OccupantNoticeId })
        {
            registry.Get(id).TargetPolicy!.TargetAssetTypeRoles.Should().Equal("occupiable-support");
            registry.Get(id).TargetPolicy!.TargetAssetTypeIds.Should().BeEmpty();
        }
    }

    [Fact]
    public void RegisterSharedUseOrderTypes_AndPoleAssetTypeIds_NullArguments_ThrowArgumentNullException()
    {
        ((Action)(() => SharedUseHostExtensions.RegisterSharedUseOrderTypes(null!))).Should().Throw<ArgumentNullException>();
        ((Action)(() => OrderOccupancyHostExtensions.PoleAssetTypeIds(null!))).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddServerSharedUseDispatch_NullServices_ThrowsArgumentNullException() =>
        ((Action)(() => SharedUseHostExtensions.AddServerSharedUseDispatch(null!))).Should().Throw<ArgumentNullException>();

    // ── Dispatch service ──────────────────────────────────────────────────────

    [Fact]
    public void AddServerSharedUseDispatch_RegistersTheServiceBehindBothPorts_Decorated_WithTheEfUnitOfWork()
    {
        var services = new ServiceCollection();

        services.AddServerSharedUseDispatch();

        services.Should().ContainSingle(d => d.ServiceType == typeof(SharedUseDispatchService));
        services.Should().ContainSingle(d => d.ServiceType == typeof(ISharedUseDispatchService))
            .Which.ImplementationFactory.Should().NotBeNull("the write port resolves to the span-and-metrics decorator");
        services.Should().ContainSingle(d => d.ServiceType == typeof(ISharedUseDispatchPlanReader));
        services.Should().ContainSingle(d => d.ServiceType == typeof(IWorkflowUnitOfWork))
            .Which.Lifetime.Should().Be(ServiceLifetime.Scoped, "one unit of work per request, on that request's context");
        services.Should().ContainSingle(d => d.ServiceType == typeof(ISharedUseFanOutTracer));
        typeof(EFWorkflowUnitOfWork).Should().Implement<IWorkflowUnitOfWork>();
    }
}
