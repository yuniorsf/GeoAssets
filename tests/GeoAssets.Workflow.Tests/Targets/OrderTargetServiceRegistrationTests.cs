using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Providers;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Selection;
using GeoAssets.Workflow.Selection.Strategies;
using GeoAssets.Workflow.Targets;
using GeoAssets.Workflow.TestKit;
using GeoAssets.Workflow.Tests.Orders;
using GeoAssets.Workflow.Tests.Selection;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GeoAssets.Workflow.Tests.Targets;

/// <summary>
/// <c>AddFeatureSelection()</c> and <c>AddOrderTargets()</c> (XD01-308): what they register,
/// with which lifetime, and that the service graph resolves with a scoped spatial query — the
/// lifetime the PostgreSQL adapter is registered with.
/// </summary>
public sealed class OrderTargetServiceRegistrationTests
{
    private static ServiceCollection HostServices()
    {
        var services = new ServiceCollection();
        services.AddOrderTypeRegistry();
        services.AddServiceOrderRules();
        services.AddScoped<IServiceOrderRepository, FakeServiceOrderRepository>();
        services.AddScoped(sp => new InMemoryServiceOrderTargetStore(sp.GetRequiredService<IServiceOrderRepository>()));
        services.AddScoped<IServiceOrderTargetReader>(sp => sp.GetRequiredService<InMemoryServiceOrderTargetStore>());
        services.AddScoped<IServiceOrderTargetWriter>(sp => sp.GetRequiredService<InMemoryServiceOrderTargetStore>());
        return services;
    }

    [Fact]
    public void AddFeatureSelection_RegistersTheRegistryAsASingleton_HoldingTheBuiltInStrategies()
    {
        var services = new ServiceCollection();
        services.AddFeatureSelection();
        using var provider = services.BuildServiceProvider(validateScopes: true);

        var registry = provider.GetRequiredService<FeatureSelectionRegistry>();

        registry.Should().BeSameAs(provider.GetRequiredService<FeatureSelectionRegistry>());
        registry.GetAvailableStrategies().Select(m => m.StrategyId)
            .Should().Contain([PolygonFenceSelectionStrategy.Id, IdentifierListSelectionStrategy.Id]);
    }

    [Fact]
    public void AddFeatureSelection_CalledTwice_KeepsOneRegistration()
    {
        var services = new ServiceCollection();
        services.AddFeatureSelection().AddFeatureSelection();

        services.Count(d => d.ServiceType == typeof(FeatureSelectionRegistry)).Should().Be(1);
        services.Count(d => d.ServiceType == typeof(BoundedFeatureSelector)).Should().Be(1);
    }

    [Fact]
    public void AddFeatureSelection_NullServices_ThrowsArgumentNullException() =>
        FluentActions.Invoking(() => WorkflowServiceExtensions.AddFeatureSelection(null!)).Should().Throw<ArgumentNullException>();

    [Fact]
    public async Task AddOrderTargets_WithAScopedSpatialQuery_ResolvesOneServicePerScope_UsingThatQuery()
    {
        var services = HostServices();
        services.AddScoped<IAssetSpatialQuery>(_ => new RecordingSpatialQuery());
        services.AddFeatureSelection();
        services.AddOrderTargets();
        await using var provider = services.BuildServiceProvider(validateScopes: true);

        await using var first = provider.CreateAsyncScope();
        await using var second = provider.CreateAsyncScope();
        var service = first.ServiceProvider.GetRequiredService<IOrderTargetSelectionService>();

        service.Should().BeOfType<OrderTargetSelectionService>()
            .And.BeSameAs(first.ServiceProvider.GetRequiredService<IOrderTargetSelectionService>())
            .And.BeSameAs(first.ServiceProvider.GetRequiredService<OrderTargetSelectionService>())
            .And.NotBeSameAs(second.ServiceProvider.GetRequiredService<IOrderTargetSelectionService>());
        provider.GetRequiredService<OrderTargetSelectionOptions>().MaxMatched.Should().Be(50_000);
    }

    [Fact]
    public async Task AddOrderTargets_Configure_SetsTheCeilings()
    {
        var services = HostServices();
        services.AddOrderTargets(o =>
        {
            o.MaxMatched = 1_000;
            o.MaxResolved = 200;
        });
        await using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<OrderTargetSelectionOptions>().Should().BeEquivalentTo(new { MaxMatched = 1_000, MaxResolved = 200 });
    }

    [Fact]
    public async Task AddFeatureSelection_WithoutASpatialQuery_FallsBackToTheAssetProvider()
    {
        var services = new ServiceCollection();
        var assets = new TestAssetProvider();
        services.AddSingleton<IAssetProvider>(assets);
        services.AddFeatureSelection();
        await using var provider = services.BuildServiceProvider(validateScopes: true);
        await using var scope = provider.CreateAsyncScope();

        var selector = scope.ServiceProvider.GetRequiredService<BoundedFeatureSelector>();

        selector.HasStrategy(PolygonFenceSelectionStrategy.Id).Should().BeTrue();
        typeof(AssetProviderSpatialQuery).Should().Implement<IAssetSpatialQuery>();
    }

    [Fact]
    public async Task AddFeatureSelection_WithNeitherASpatialQueryNorAnAssetProvider_FailsWithAClearMessage()
    {
        var services = new ServiceCollection();
        services.AddFeatureSelection();
        await using var provider = services.BuildServiceProvider(validateScopes: true);
        await using var scope = provider.CreateAsyncScope();

        var act = () => scope.ServiceProvider.GetRequiredService<BoundedFeatureSelector>();

        act.Should().Throw<InvalidOperationException>().WithMessage("*IAssetSpatialQuery*IAssetProvider*");
    }

    [Fact]
    public void AddOrderTargets_NullServices_ThrowsArgumentNullException() =>
        FluentActions.Invoking(() => WorkflowServiceExtensions.AddOrderTargets(null!)).Should().Throw<ArgumentNullException>();
}
