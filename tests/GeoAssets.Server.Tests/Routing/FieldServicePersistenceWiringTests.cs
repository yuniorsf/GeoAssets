using FluentAssertions;
using GeoAssets.FieldService.Contractors;
using GeoAssets.FieldService.FieldWork;
using GeoAssets.FieldService.Infrastructure;
using GeoAssets.FieldService.Persistence;
using GeoAssets.Workflow;
using GeoAssets.Workflow.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GeoAssets.Server.Tests.Routing;

/// <summary>
/// XD01-381 on the real <c>Program</c>: the FieldService vertical's <c>FieldServiceDbContext</c>, its five stores and the inspection
/// commit belong to <c>GeoAssets.FieldService.Server</c> alone — the kernel host registers none of them, with every module or with
/// <c>GeoAssets:Modules = []</c> — and the kernel's <c>AddWorkflowPersistence</c> registers none either.
/// </summary>
[Trait("Category", "RouteInventory")]
public sealed class FieldServicePersistenceWiringTests
{
    private static readonly Type[] VerticalTypes =
    [
        typeof(FieldServiceDbContext),
        typeof(DbContextOptions<FieldServiceDbContext>),
        typeof(IAmbientDbTransaction),
        typeof(IContractorUsLedgerRepository),
        typeof(IUsCatalogRepository),
        typeof(IContractorUsPostingRepository),
        typeof(IPavingMeasurementRepository),
        typeof(IManholeAuditRepository),
        typeof(EFContractorUsLedgerRepository),
        typeof(EFUsCatalogRepository),
        typeof(EFContractorUsPostingRepository),
        typeof(EFPavingMeasurementRepository),
        typeof(EFManholeAuditRepository),
        typeof(InspectionCommitService),
    ];

    private static readonly string[] FieldServiceTables =
        ["ContractorUsLedgerEntries", "UsCatalogItems", "field_contractor_us_postings", "field_icerp_paving_measurements", "field_manhole_audits"];

    [Fact]
    public async Task Server_WithEveryModule_RegistersNeitherTheFieldServiceContextNorAnyOfItsStores()
    {
        var services = await ServerHostFixture.GetNeutralOperatorEnabledServicesAsync();
        await using var scope = services.CreateAsyncScope();

        AssertNoneIsRegistered(scope.ServiceProvider);
    }

    [Fact]
    public async Task Server_WithEveryModuleDisabled_RegistersNeitherTheFieldServiceContextNorAnyOfItsStores()
    {
        var factory = await ServerHostFixture.GetKernelOnlyFactoryAsync();
        await using var scope = factory.Services.CreateAsyncScope();

        AssertNoneIsRegistered(scope.ServiceProvider);
    }

    [Fact]
    public void AddWorkflowPersistence_Alone_RegistersNoFieldServiceType()
    {
        var services = new ServiceCollection();

        services.AddWorkflowPersistence(options => options.UseNpgsql("Host=localhost;Port=1;Database=wiring_test;Username=boot;Password=boot;Timeout=1"));

        var registered = services.SelectMany(descriptor => new[] { descriptor.ServiceType, descriptor.ImplementationType }.OfType<Type>()).ToList();
        registered.Should().NotIntersectWith(VerticalTypes, "the kernel's persistence registration knows no FieldService store, context or commit");
        registered.Should().NotContain(
            type => (type.Namespace ?? string.Empty).StartsWith("GeoAssets.FieldService", StringComparison.Ordinal),
            "nothing of the FieldService vertical is registered by the engine's persistence");
    }

    private static void AssertNoneIsRegistered(IServiceProvider provider)
    {
        foreach (var type in VerticalTypes)
            provider.GetService(type).Should().BeNull($"{type.Name} is not registered by the kernel host");

        provider.GetRequiredService<ServiceOrderDbContext>().Model.GetEntityTypes()
            .Select(entity => entity.GetTableName()).Should().NotIntersectWith(FieldServiceTables);
    }
}
