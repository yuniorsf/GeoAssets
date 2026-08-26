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
    public void AddGeoAssetsPanelState_ResolvedServices_AreTheExpectedImplementations()
    {
        var services = new ServiceCollection();
        services.AddGeoAssetsPanelState();
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IFeatureSelectionState>().Should().BeOfType<FeatureSelectionState>();
        provider.GetRequiredService<ICurrentMapContext>().Should().BeOfType<CurrentMapContext>();
    }
}
