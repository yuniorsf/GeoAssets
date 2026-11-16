using FluentAssertions;
using GeoAssets.Shared.Interfaces;
using GeoAssets.Shared.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GeoAssets.Shared.Tests.Services;

public class PanelStateServiceExtensionsTests
{
    [Fact]
    public void AddGeoAssetsPanelState_RegistersFeatureSelectionStateAsScoped()
    {
        var services = new ServiceCollection();
        services.AddGeoAssetsPanelState();
        using var provider = services.BuildServiceProvider();

        using var scope1 = provider.CreateScope();
        var first  = scope1.ServiceProvider.GetRequiredService<IFeatureSelectionState>();
        var second = scope1.ServiceProvider.GetRequiredService<IFeatureSelectionState>();

        using var scope2 = provider.CreateScope();
        var thirdInAnotherScope = scope2.ServiceProvider.GetRequiredService<IFeatureSelectionState>();

        first.Should().BeSameAs(second);
        first.Should().NotBeSameAs(thirdInAnotherScope);
    }

    [Fact]
    public void AddGeoAssetsPanelState_RegistersCurrentMapContextAsSingleton()
    {
        var services = new ServiceCollection();
        services.AddGeoAssetsPanelState();
        using var provider = services.BuildServiceProvider();

        using var scope1 = provider.CreateScope();
        using var scope2 = provider.CreateScope();

        var fromScope1 = scope1.ServiceProvider.GetRequiredService<ICurrentMapContext>();
        var fromScope2 = scope2.ServiceProvider.GetRequiredService<ICurrentMapContext>();

        fromScope1.Should().BeSameAs(fromScope2);
    }

    [Fact]
    public void AddGeoAssetsPanelState_RegistersPendingDrawTypeStateAsScoped()
    {
        var services = new ServiceCollection();
        services.AddGeoAssetsPanelState();
        using var provider = services.BuildServiceProvider();

        using var scope1 = provider.CreateScope();
        var first  = scope1.ServiceProvider.GetRequiredService<IPendingDrawTypeState>();
        var second = scope1.ServiceProvider.GetRequiredService<IPendingDrawTypeState>();

        using var scope2 = provider.CreateScope();
        var thirdInAnotherScope = scope2.ServiceProvider.GetRequiredService<IPendingDrawTypeState>();

        first.Should().BeSameAs(second);
        first.Should().NotBeSameAs(thirdInAnotherScope);
    }

    [Fact]
    public void AddGeoAssetsPanelState_ResolvedServices_AreTheExpectedImplementations()
    {
        var services = new ServiceCollection();
        services.AddGeoAssetsPanelState();
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IFeatureSelectionState>().Should().BeOfType<FeatureSelectionState>();
        provider.GetRequiredService<ICurrentMapContext>().Should().BeOfType<CurrentMapContext>();
        provider.GetRequiredService<IPendingDrawTypeState>().Should().BeOfType<PendingDrawTypeState>();
    }

    [Fact]
    public void AddGeoAssetsPanelState_RegistersMapSelectionSetAsScoped()
    {
        var services = new ServiceCollection();
        services.AddGeoAssetsPanelState();
        using var provider = services.BuildServiceProvider();

        using var scope1 = provider.CreateScope();
        var first  = scope1.ServiceProvider.GetRequiredService<IMapSelectionSet>();
        var second = scope1.ServiceProvider.GetRequiredService<IMapSelectionSet>();

        using var scope2 = provider.CreateScope();
        var thirdInAnotherScope = scope2.ServiceProvider.GetRequiredService<IMapSelectionSet>();

        first.Should().BeOfType<MapSelectionSet>().And.BeSameAs(second);
        first.Should().NotBeSameAs(thirdInAnotherScope);
        first.MaxCount.Should().Be(10_000);
    }

    [Fact]
    public void AddGeoAssetsPanelState_WithoutAHostResolver_FallsBackToTheUnavailableResolver()
    {
        var services = new ServiceCollection();
        services.AddGeoAssetsPanelState();
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IMapSelectionResolver>().Should().BeOfType<UnavailableMapSelectionResolver>();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AddGeoAssetsPanelState_HostResolver_WinsWhicheverSideOfTheCallItIsRegistered(bool registerBefore)
    {
        var hostResolver = new UnavailableMapSelectionResolver();
        var services = new ServiceCollection();
        if (registerBefore) services.AddSingleton<IMapSelectionResolver>(hostResolver);
        services.AddGeoAssetsPanelState();
        if (!registerBefore) services.AddSingleton<IMapSelectionResolver>(hostResolver);
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IMapSelectionResolver>().Should().BeSameAs(hostResolver);
    }
}
