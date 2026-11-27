using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Providers;
using GeoAssets.Core.Rendering;
using GeoAssets.Core.Services;
using GeoAssets.Provider.WMS;
using GeoAssets.Shared.Interfaces;
using GeoAssets.Shared.Services;
using GeoAssets.Shared.Services.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GeoAssets.Shared.Tests.Services.Rendering;

/// <summary>
/// XD01-422 — <see cref="ProviderMapLayerRendererChain"/> and its registration: the first registered renderer
/// that accepts a provider draws it, a provider nothing accepts resolves to none, a renderer registered by a host
/// joins the chain without any other change (open for extension), and the registered chain sends WMS to
/// <see cref="WmsMapLayerRenderer"/> and everything else to <see cref="VectorFeatureMapLayerRenderer"/> — also
/// through the decorator chain.
/// </summary>
public sealed class ProviderMapLayerRendererChainTests
{
    [Fact]
    public void Resolve_FirstRendererThatAccepts_Wins()
    {
        var declines = new StubRenderer(accepts: false);
        var first = new StubRenderer(accepts: true);
        var second = new StubRenderer(accepts: true);
        var chain = new ProviderMapLayerRendererChain([declines, first, second]);

        chain.Resolve(new FakeLayerProvider()).Should().BeSameAs(first);
        declines.Asked.Should().Be(1);
        second.Asked.Should().Be(0, "the chain stops at the first match");
    }

    [Fact]
    public void Resolve_NoRendererAccepts_ReturnsNull()
    {
        var chain = new ProviderMapLayerRendererChain([new StubRenderer(accepts: false)]);

        chain.Resolve(new FakeLayerProvider()).Should().BeNull();
    }

    [Fact]
    public void Resolve_RejectsNull()
    {
        var act = () => new ProviderMapLayerRendererChain([]).Resolve(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Registered_WmsGoesToTheWmsRenderer_VectorToTheVectorRenderer_NothingToNone()
    {
        using var provider = Services().BuildServiceProvider();
        using var scope = provider.CreateScope();
        var chain = scope.ServiceProvider.GetRequiredService<ProviderMapLayerRendererChain>();

        chain.Resolve(new WmsAssetProvider("https://srv/wms", "geoassets:feature")).Should().BeOfType<WmsMapLayerRenderer>();
        chain.Resolve(new FakeWmsLayerProvider()).Should().BeOfType<WmsMapLayerRenderer>();
        chain.Resolve(new FakeLayerProvider()).Should().BeOfType<VectorFeatureMapLayerRenderer>();
        chain.Resolve(new CapabilityDeclaringLayerProvider(ProviderRenderCapabilities.None)).Should().BeNull();
    }

    [Fact]
    public void Registered_ResolvesThroughTheDecoratorChain()
    {
        // The ticket's decorator bug: ValidatingAssetProvider(wms) is not `is IWmsProvider`, yet must render as WMS.
        using var provider = Services().BuildServiceProvider();
        using var scope = provider.CreateScope();
        var chain = scope.ServiceProvider.GetRequiredService<ProviderMapLayerRendererChain>();

        chain.Resolve(new ValidatingAssetProvider(new WmsAssetProvider("https://srv/wms", "geoassets:feature")))
            .Should().BeOfType<WmsMapLayerRenderer>();
        chain.Resolve(new ValidatingAssetProvider(new FakeLayerProvider())).Should().BeOfType<VectorFeatureMapLayerRenderer>();
    }

    [Fact]
    public void Registered_AHostRenderer_JoinsTheChainWithoutOtherChanges()
    {
        var services = Services();
        services.AddScoped<IProviderMapLayerRenderer, VectorTilesRenderer>();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var chain = scope.ServiceProvider.GetRequiredService<ProviderMapLayerRendererChain>();

        chain.Resolve(new CapabilityDeclaringLayerProvider(ProviderRenderCapabilities.VectorTiles))
            .Should().BeOfType<VectorTilesRenderer>();
    }

    [Fact]
    public void Registration_TwiceRegistersNothingTwice()
    {
        var services = Services();
        services.AddProviderMapLayerRenderers();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetServices<IProviderMapLayerRenderer>().Select(r => r.GetType())
            .Should().Equal(typeof(WmsMapLayerRenderer), typeof(VectorFeatureMapLayerRenderer), typeof(HybridLodMapLayerRenderer), typeof(LocalRasterTileMapLayerRenderer));
    }

    [Fact]
    public void Registered_ALocalRasterProvider_GoesToTheLocalRasterRenderer_OnlyWhereTheHostRegistersItsInterop()
    {
        // XD01-465: chosen by capability; the Web (no IMapLocalTileLayerInterop) draws none, nor sends it anywhere else.
        using (var web = Services().BuildServiceProvider())
        using (var scope = web.CreateScope())
        {
            var chain = scope.ServiceProvider.GetRequiredService<ProviderMapLayerRendererChain>();
            chain.Resolve(new FakeLocalRasterLayerProvider()).Should().BeNull();
        }

        var services = Services();
        services.AddScoped<IMapLocalTileLayerInterop, RecordingLocalTileLayerInterop>();
        using var maui = services.BuildServiceProvider();
        using var mauiScope = maui.CreateScope();
        var local = mauiScope.ServiceProvider.GetRequiredService<ProviderMapLayerRendererChain>();
        local.Resolve(new FakeLocalRasterLayerProvider()).Should().BeOfType<LocalRasterTileMapLayerRenderer>();
        local.Resolve(new ValidatingAssetProvider(new FakeLocalRasterLayerProvider())).Should().BeOfType<LocalRasterTileMapLayerRenderer>();
        local.Resolve(new FakeWmsLayerProvider()).Should().BeOfType<WmsMapLayerRenderer>();
        local.Resolve(new FakeLayerProvider()).Should().BeOfType<VectorFeatureMapLayerRenderer>();
    }

    [Fact]
    public void Registered_TheHybridRendererIsAskedFirst_AndTakesTheKernelOnceTheHostRegistersTheTileInterop()
    {
        // XD01-436: registered last, asked first (its precedence) — and a kernel provider falls back to the vector
        // renderer while the host has no tile interop.
        var services = Services();
        services.Configure<MapInteropOptions>(o => o.MapLibre.StyleUrl = "https://gw.example/tiles/style.json");
        services.Configure<MapInteropOptions>(o => o.RenderMode = MapRenderMode.MapLibre);
        using (var withoutTiles = services.BuildServiceProvider())
        using (var scope = withoutTiles.CreateScope())
        {
            var chain = scope.ServiceProvider.GetRequiredService<ProviderMapLayerRendererChain>();
            chain.Renderers[0].Should().BeOfType<HybridLodMapLayerRenderer>();
            chain.Resolve(new KernelLayerProvider()).Should().BeOfType<VectorFeatureMapLayerRenderer>();
        }

        services.AddSingleton<IMapTileLayerInterop>(sp => new FakeTileLayerInterop(new LayerRecordingMapInterop()));
        using var withTiles = services.BuildServiceProvider();
        using var tilesScope = withTiles.CreateScope();
        var tiled = tilesScope.ServiceProvider.GetRequiredService<ProviderMapLayerRendererChain>();
        tiled.Resolve(new KernelLayerProvider()).Should().BeOfType<HybridLodMapLayerRenderer>();
        tiled.Resolve(new FakeLayerProvider()).Should().BeOfType<VectorFeatureMapLayerRenderer>();
        tiled.Resolve(new FakeWmsLayerProvider()).Should().BeOfType<WmsMapLayerRenderer>();
    }

    [Fact]
    public void Registration_RejectsNull()
    {
        var act = () => ProviderMapLayerRenderingServiceExtensions.AddProviderMapLayerRenderers(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Request_For_ReadsTheEntrysOwnProvider_AndNamesTheLayerAfterTheEntry()
    {
        var entry = new ProviderEntry { Name = "Local", Provider = new FakeLayerProvider() };

        var request = MapLayerRenderRequest.For("map", entry);

        request.Source.Should().BeSameAs(entry.Provider);
        request.MapDivId.Should().Be("map");
        request.LayerId.Should().Be(entry.Id.ToString());
        FluentActions.Invoking(() => MapLayerRenderRequest.For("map", null!)).Should().Throw<ArgumentNullException>();
    }

    private static ServiceCollection Services()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IMapInterop>(new LayerRecordingMapInterop());
        services.AddSingleton<FeatureRenderPipeline>();
        services.AddProviderMapLayerRenderers();
        return services;
    }

    private sealed class StubRenderer(bool accepts) : IProviderMapLayerRenderer
    {
        public int Asked { get; private set; }
        public bool ClearedWithFeatures => true;

        public bool CanRender(IAssetProvider provider)
        {
            Asked++;
            return accepts;
        }

        public Task<int> RenderAsync(MapLayerRenderRequest request, CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task RemoveAsync(MapLayerRenderRequest request, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<int> RenderViewportAsync(MapLayerRenderRequest request, ViewportQuery viewport, CancellationToken cancellationToken = default) => Task.FromResult(0);
    }

    /// <summary>A renderer a host adds for a new capability — no switch anywhere is edited for it.</summary>
    private sealed class VectorTilesRenderer : IProviderMapLayerRenderer
    {
        public bool ClearedWithFeatures => false;
        public bool CanRender(IAssetProvider provider) => provider.GetRenderCapabilities().HasFlag(ProviderRenderCapabilities.VectorTiles);
        public Task<int> RenderAsync(MapLayerRenderRequest request, CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task RemoveAsync(MapLayerRenderRequest request, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<int> RenderViewportAsync(MapLayerRenderRequest request, ViewportQuery viewport, CancellationToken cancellationToken = default) => Task.FromResult(0);
    }
}
