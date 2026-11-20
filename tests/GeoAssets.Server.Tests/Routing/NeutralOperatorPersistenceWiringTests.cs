using FluentAssertions;
using GeoAssets.NeutralOperator.Application.Occupancy;
using GeoAssets.NeutralOperator.Application.Occupancy.Saga;
using GeoAssets.NeutralOperator.Domain.Occupancy;
using GeoAssets.NeutralOperator.Persistence;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GeoAssets.Server.Tests.Routing;

/// <summary>
/// XD01-380 on the real <c>Program</c>: <c>NeutralOperatorDbContext</c> and the occupancy ports on it are registered by the
/// neutral-operator module, over the kernel context's connection — and by nothing else, so a kernel-only host has neither.
/// </summary>
[Trait("Category", "RouteInventory")]
public sealed class NeutralOperatorPersistenceWiringTests
{
    [Fact]
    public async Task Server_WithTheNeutralOperatorModule_ResolvesTheVerticalsContextAndOccupancyPorts_OverTheKernelsConnection()
    {
        var services = await ServerHostFixture.GetNeutralOperatorEnabledServicesAsync();
        await using var scope = services.CreateAsyncScope();
        var provider = scope.ServiceProvider;

        var neutralOperator = provider.GetRequiredService<NeutralOperatorDbContext>();
        var kernel = provider.GetRequiredService<ServiceOrderDbContext>();

        neutralOperator.Database.IsNpgsql().Should().BeTrue();
        neutralOperator.Database.GetDbConnection().Should().BeSameAs(kernel.Database.GetDbConnection());
        provider.GetRequiredService<IPoleOccupancyReader>().Should().BeOfType<EFPoleOccupancyRepository>()
            .And.BeSameAs(provider.GetRequiredService<IPoleOccupancyWriter>());
        provider.GetRequiredService<IOccupancyChangeSetReader>().Should().BeOfType<EFOccupancyChangeSetRepository>()
            .And.BeSameAs(provider.GetRequiredService<IOccupancyChangeSetWriter>())
            .And.BeSameAs(provider.GetRequiredService<IOccupancyChangeSetCloser>());
        provider.GetRequiredService<IOccupancyCommitService>().Should().BeOfType<ObservableOccupancyCommitService>();
        provider.GetRequiredService<IWorkflowUnitOfWork>().Should().BeOfType<EFWorkflowUnitOfWork>();
        provider.GetServices<IOrderLifecycleHook>().Should().ContainSingle(hook => hook is OccupancyReleaseLifecycleHook);
        neutralOperator.Database.GetMigrations().Should().HaveCount(2)
            .And.SatisfyRespectively(
                baseline => baseline.Should().EndWith("_AdoptOccupancyTables"),
                saga => saga.Should().EndWith("_AddOccupancyApplySagas"));
        provider.GetRequiredService<IOccupancyApplySagaStore>().Should().BeOfType<EFOccupancyApplySagaStore>()
            .And.BeSameAs(provider.GetRequiredService<IOccupancyApplySagaMonitor>());
        provider.GetRequiredService<IOccupancyApplySagaService>().Should().BeOfType<ObservableOccupancyApplySagaService>();
        provider.GetServices<ITransitionGuard>().Should().ContainSingle(guard => guard is OccupancyApplyPendingTransitionGuard);
    }

    [Fact]
    public async Task Server_WithEveryModuleDisabled_BootsWithoutTheVerticalsContextOrOccupancyPorts()
    {
        var factory = await ServerHostFixture.GetKernelOnlyFactoryAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var provider = scope.ServiceProvider;

        provider.GetService<NeutralOperatorDbContext>().Should().BeNull();
        provider.GetService<DbContextOptions<NeutralOperatorDbContext>>().Should().BeNull();
        provider.GetService<IAmbientDbTransaction>().Should().BeNull();
        provider.GetService<IPoleOccupancyReader>().Should().BeNull();
        provider.GetService<IPoleOccupancyWriter>().Should().BeNull();
        provider.GetService<IOccupancyChangeSetReader>().Should().BeNull();
        provider.GetService<IOccupancyChangeSetWriter>().Should().BeNull();
        provider.GetService<IOccupancyChangeSetCloser>().Should().BeNull();
        provider.GetServices<IOrderLifecycleHook>().Should().NotContain(hook => hook is OccupancyReleaseLifecycleHook);
        provider.GetRequiredService<ServiceOrderDbContext>().Model.GetEntityTypes()
            .Select(entity => entity.GetTableName()).Should().NotContain(["field_pole_occupancies", "field_pole_occupancy_entries", "ServiceOrderOccupancyChangeSets"]);
    }

    [Fact]
    public async Task Server_WithOnlyTheNeutralOperatorModuleDisabled_HasNoVerticalsContextEither()
    {
        var services = await ServerHostFixture.GetNeutralOperatorDisabledServicesAsync();
        await using var scope = services.CreateAsyncScope();

        scope.ServiceProvider.GetService<NeutralOperatorDbContext>().Should().BeNull();
        scope.ServiceProvider.GetService<IPoleOccupancyReader>().Should().BeNull();
        scope.ServiceProvider.GetService<IOccupancyChangeSetCloser>().Should().BeNull();
    }
}
