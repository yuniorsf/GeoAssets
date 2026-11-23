using FluentAssertions;
using GeoAssets.Shared.Interfaces;
using GeoAssets.Shared.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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

    // ── Layer display and order focus (XD01-311) ─────────────────────────────

    [Fact]
    public void AddGeoAssetsPanelState_RegistersMapLayerDisplayServiceAsScoped_OverTheHostsMapInterop()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddScoped<IMapInterop, RecordingMapInterop>();
        services.AddGeoAssetsPanelState();
        using var provider = services.BuildServiceProvider(validateScopes: true);

        using var scopeA = provider.CreateScope();
        using var scopeB = provider.CreateScope();
        var first = scopeA.ServiceProvider.GetRequiredService<MapLayerDisplayService>();

        scopeA.ServiceProvider.GetRequiredService<MapLayerDisplayService>().Should().BeSameAs(first);
        scopeB.ServiceProvider.GetRequiredService<MapLayerDisplayService>().Should().NotBeSameAs(first);
    }

    // ── Reference layers (XD01-331) ──────────────────────────────────────────

    private static ServiceProvider ReferenceLayerHost(Action<IServiceCollection>? before = null, Action<IServiceCollection>? after = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IMapInterop, RecordingMapInterop>();
        services.AddSingleton<GeoAssets.Identity.Authorization.Services.ICrossOrgEntitlementsService,
            GeoAssets.Identity.Authorization.Services.NoCrossOrgEntitlementsService>();
        services.AddSingleton<GeoAssets.Identity.Authorization.Services.IGeoAuthorizationService>(
            new GeoAssets.Shared.Tests.Services.ReferenceLayers.FakeAuthorization(null!));
        before?.Invoke(services);
        services.AddGeoAssetsPanelState();
        after?.Invoke(services);
        return services.BuildServiceProvider(validateScopes: true);
    }

    [Fact]
    public void AddGeoAssetsPanelState_RegistersTheReferenceLayerServiceAsScoped_WithTheUnavailableClientByDefault()
    {
        using var provider = ReferenceLayerHost();
        using var scopeA = provider.CreateScope();
        using var scopeB = provider.CreateScope();

        var first = scopeA.ServiceProvider.GetRequiredService<GeoAssets.Shared.Services.ReferenceLayers.ReferenceLayerService>();

        scopeA.ServiceProvider.GetRequiredService<GeoAssets.Shared.Services.ReferenceLayers.ReferenceLayerService>().Should().BeSameAs(first);
        scopeB.ServiceProvider.GetRequiredService<GeoAssets.Shared.Services.ReferenceLayers.ReferenceLayerService>().Should().NotBeSameAs(first);
        scopeA.ServiceProvider.GetRequiredService<GeoAssets.Shared.Services.ReferenceLayers.IReferenceFeatureClient>()
            .Should().BeOfType<GeoAssets.Shared.Services.ReferenceLayers.UnavailableReferenceFeatureClient>();
    }

    [Fact]
    public void AddGeoAssetsPanelState_KeepsAHostsOwnReferenceFeatureClient_RegisteredBefore()
    {
        using var provider = ReferenceLayerHost(before: s =>
            s.AddScoped<GeoAssets.Shared.Services.ReferenceLayers.IReferenceFeatureClient>(
                _ => new GeoAssets.Shared.Tests.Services.ReferenceLayers.FakeReferenceFeatureClient()));
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<GeoAssets.Shared.Services.ReferenceLayers.IReferenceFeatureClient>()
            .Should().BeOfType<GeoAssets.Shared.Tests.Services.ReferenceLayers.FakeReferenceFeatureClient>();
    }

    [Fact]
    public async Task AddGeoAssetsPanelState_AHostWithoutEntitlements_OffersNoReferenceLayer()
    {
        using var provider = ReferenceLayerHost();
        using var scope = provider.CreateScope();

        var offered = await scope.ServiceProvider.GetRequiredService<GeoAssets.Shared.Services.ReferenceLayers.ReferenceLayerService>()
            .LoadOrganizationsAsync();

        offered.Should().BeEmpty("the MAUI app has no entitlement source, so its map stays as it was");
    }

    [Fact]
    public void AddGeoAssetsPanelState_RegistersMapOrderFocusAsScoped()
    {
        var services = new ServiceCollection();
        services.AddGeoAssetsPanelState();

        var descriptor = services.Should().ContainSingle(d => d.ServiceType == typeof(IMapOrderFocus)).Subject;
        descriptor.Lifetime.Should().Be(ServiceLifetime.Scoped);
        descriptor.ImplementationType.Should().Be<MapOrderFocusService>();
    }

    [Fact]
    public void AddGeoAssetsPanelState_RegistersMapSelectionFenceMemoryAsScoped_OverTheScopedSelection()
    {
        var services = new ServiceCollection();
        services.AddGeoAssetsPanelState();

        var descriptor = services.Should().ContainSingle(d => d.ServiceType == typeof(MapSelectionFenceMemory)).Subject;
        descriptor.Lifetime.Should().Be(ServiceLifetime.Scoped);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var memory = scope.ServiceProvider.GetRequiredService<MapSelectionFenceMemory>();
        memory.Remember([[[0, 0], [1, 0], [1, 1], [0, 0]]]);
        scope.ServiceProvider.GetRequiredService<IMapSelectionSet>().Add(["a"]);
        memory.Rings.Should().BeNull("the memory watches the same scoped selection the map changes");
    }
}
