using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Providers;
using GeoAssets.Core.Rendering;
using GeoAssets.Provider.WMS;
using GeoAssets.Shared.Services.Rendering;
using Xunit;

namespace GeoAssets.Shared.Tests.Services.Rendering;

/// <summary>
/// XD01-465 — <see cref="LocalRasterTileMapLayerRenderer"/>: chosen by capability (<see cref="ProviderRenderCapabilities.RasterTiles"/>
/// on an <see cref="ILocalRasterTileProvider"/>, through the decorator chain) and only where the host draws local tile
/// layers; draws the entry's tile layer from its own provider, fits the map to the layer's bounds the first time, and
/// takes it off again.
/// </summary>
public sealed class LocalRasterTileMapLayerRendererTests
{
    private const string Div = "geoassets-map";

    private readonly LayerRecordingMapInterop _map = new();
    private readonly RecordingLocalTileLayerInterop _tiles = new();

    private LocalRasterTileMapLayerRenderer Renderer() => new(_map, _tiles);

    [Fact]
    public void CanRender_ALocalRasterProvider_AlsoBehindTheDecoratorChain()
    {
        Renderer().CanRender(new FakeLocalRasterLayerProvider()).Should().BeTrue();
        Renderer().CanRender(new ValidatingAssetProvider(new FakeLocalRasterLayerProvider())).Should().BeTrue();
    }

    [Fact]
    public void CannotRender_WithoutTheHostsLocalTileInterop()
    {
        new LocalRasterTileMapLayerRenderer(_map).CanRender(new FakeLocalRasterLayerProvider()).Should().BeFalse("the Web registers no local tile interop");
    }

    [Fact]
    public void CannotRender_ALocalRasterProviderDeclaringNoRasterTiles()
    {
        Renderer().CanRender(new FakeLocalRasterLayerProvider(ProviderRenderCapabilities.VectorFeatures)).Should().BeFalse();
    }

    [Fact]
    public void CannotRender_WmsVectorOrNothing()
    {
        var renderer = Renderer();

        renderer.CanRender(new WmsAssetProvider("https://srv/wms", "geoassets:feature")).Should().BeFalse("RasterTiles without the local tile source is WMS's");
        renderer.CanRender(new CapabilityDeclaringLayerProvider(ProviderRenderCapabilities.RasterTiles)).Should().BeFalse();
        renderer.CanRender(new FakeLayerProvider()).Should().BeFalse();
        FluentActions.Invoking(() => renderer.CanRender(null!)).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void IsNotClearedWithFeatures()
    {
        Renderer().ClearedWithFeatures.Should().BeFalse("a tile layer survives ClearAllFeaturesAsync");
    }

    [Fact]
    public async Task RenderAsync_AddsTheEntrysTileLayerFromItsOwnProvider_AndFitsItsBoundsOnce()
    {
        var provider = new FakeLocalRasterLayerProvider(bounds: new BoundingBox(-9.5, 38.6, -9.0, 38.9));
        var entry = new ProviderEntry { Name = "Lisboa", Provider = provider };
        var renderer = Renderer();

        var rendered = await renderer.RenderAsync(new MapLayerRenderRequest(Div, entry, new ValidatingAssetProvider(provider)));
        await renderer.RenderAsync(MapLayerRenderRequest.For(Div, entry));

        rendered.Should().Be(0, "a tile layer draws no discrete features");
        _tiles.Adds.Should().HaveCount(2);
        _tiles.Adds[0].DivId.Should().Be(Div);
        _tiles.Adds[0].LayerId.Should().Be(entry.Id.ToString());
        _tiles.Adds[0].Tiles.Should().BeSameAs(provider.Tiles);
        _tiles.Adds[0].Layer.Should().BeSameAs(provider.RasterTileLayer);
        _map.FitCalls.Should().ContainSingle("only the first draw of an entry moves the map").Which.Bbox.Should().Equal(-9.5, 38.6, -9.0, 38.9);
        _map.FeatureBatches.Should().BeEmpty();
    }

    [Fact]
    public async Task RenderAsync_ALayerOfUnknownBounds_DoesNotMoveTheMap()
    {
        var entry = new ProviderEntry { Name = "World", Provider = new FakeLocalRasterLayerProvider() };

        await Renderer().RenderAsync(MapLayerRenderRequest.For(Div, entry));

        _tiles.Adds.Should().ContainSingle();
        _map.FitCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task RenderAsync_AnEntryThatIsNotLocalRaster_Throws()
    {
        var entry = new ProviderEntry { Name = "Local", Provider = new FakeLayerProvider() };

        var act = () => Renderer().RenderAsync(MapLayerRenderRequest.For(Div, entry));

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*Local*");
        _tiles.Adds.Should().BeEmpty();
    }

    [Fact]
    public async Task WithoutTheInterop_RenderAndRemoveThrow()
    {
        var entry = new ProviderEntry { Name = "Pack", Provider = new FakeLocalRasterLayerProvider() };
        var renderer = new LocalRasterTileMapLayerRenderer(_map);

        await renderer.Invoking(r => r.RenderAsync(MapLayerRenderRequest.For(Div, entry))).Should().ThrowAsync<InvalidOperationException>();
        await renderer.Invoking(r => r.RemoveAsync(MapLayerRenderRequest.For(Div, entry))).Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task RemoveAsync_RemovesTheEntrysTileLayer()
    {
        var entry = new ProviderEntry { Name = "Pack", Provider = new FakeLocalRasterLayerProvider() };

        await Renderer().RemoveAsync(MapLayerRenderRequest.For(Div, entry));

        _tiles.Removes.Should().Equal((Div, entry.Id.ToString()));
    }

    [Fact]
    public async Task RenderViewportAsync_DrawsNothing()
    {
        var entry = new ProviderEntry { Name = "Pack", Provider = new FakeLocalRasterLayerProvider() };

        var rendered = await Renderer().RenderViewportAsync(MapLayerRenderRequest.For(Div, entry), new ViewportQuery(new BoundingBox(-1, -1, 1, 1), 12));

        rendered.Should().Be(0);
        _tiles.Adds.Should().BeEmpty();
    }

    [Fact]
    public async Task RenderAndRemove_RejectNull()
    {
        var renderer = Renderer();

        await renderer.Invoking(r => r.RenderAsync(null!)).Should().ThrowAsync<ArgumentNullException>();
        await renderer.Invoking(r => r.RemoveAsync(null!)).Should().ThrowAsync<ArgumentNullException>();
    }
}
