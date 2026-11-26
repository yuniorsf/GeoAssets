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
/// XD01-422 — <see cref="WmsMapLayerRenderer"/>: draws a WMS provider as a tile layer named after its pool entry,
/// exactly as the replaced <c>is IWmsProvider</c> branches did (same layer id, base URL, LAYERS and FORMAT), also
/// when the provider sits behind the decorator chain, and leaves vector providers to another renderer.
/// </summary>
public sealed class WmsMapLayerRendererTests
{
    private const string Div = "geoassets-map";

    private readonly LayerRecordingMapInterop _map = new();

    private WmsMapLayerRenderer Renderer() => new(_map);

    [Fact]
    public void CanRender_TheRealWmsProvider()
    {
        Renderer().CanRender(new WmsAssetProvider("https://srv/api/geoassets/wms", "geoassets:feature")).Should().BeTrue();
    }

    [Fact]
    public void CanRender_AnIWmsProviderWithoutDeclaredCapabilities()
    {
        Renderer().CanRender(new FakeWmsLayerProvider()).Should().BeTrue();
    }

    [Fact]
    public void CanRender_AWmsProviderBehindTheDecoratorChain()
    {
        // ValidatingAssetProvider implements neither IWmsProvider nor IProviderCapabilities itself —
        // a plain `is IWmsProvider` on it is false; the renderer must look through it (UnwrapToConcrete).
        IAssetProvider decorated = new ValidatingAssetProvider(new WmsAssetProvider("https://srv/api/geoassets/wms", "geoassets:feature"));

        (decorated is IWmsProvider).Should().BeFalse("the decorator hides the WMS provider from a plain type test");
        Renderer().CanRender(decorated).Should().BeTrue();
    }

    [Fact]
    public void CannotRender_AVectorProvider()
    {
        Renderer().CanRender(new FakeLayerProvider()).Should().BeFalse();
    }

    [Fact]
    public void CannotRender_ARasterProviderThatIsNotWms()
    {
        // RasterTiles alone isn't enough — the renderer needs the WMS endpoint parameters.
        Renderer().CanRender(new CapabilityDeclaringLayerProvider(ProviderRenderCapabilities.RasterTiles)).Should().BeFalse();
    }

    [Fact]
    public void CannotRender_AWmsProviderDeclaringNoRasterTiles()
    {
        Renderer().CanRender(new WmsDeclaringVector()).Should().BeFalse();
    }

    [Fact]
    public void CanRender_RejectsNull()
    {
        var act = () => Renderer().CanRender(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void IsNotClearedWithFeatures()
    {
        Renderer().ClearedWithFeatures.Should().BeFalse("a WMS tile layer survives ClearAllFeaturesAsync");
    }

    [Fact]
    public async Task RenderAsync_AddsTheWmsLayerNamedAfterTheEntry()
    {
        var entry = new ProviderEntry { Name = "WMS", Provider = new WmsAssetProvider("https://srv/api/geoassets/wms", "geoassets:feature", "image/jpeg") };

        var rendered = await Renderer().RenderAsync(MapLayerRenderRequest.For(Div, entry));

        rendered.Should().Be(0, "a tile layer draws no discrete features");
        _map.WmsAdds.Should().ContainSingle();
        var call = _map.WmsAdds[0];
        call.DivId.Should().Be(Div);
        call.LayerId.Should().Be(entry.Id.ToString());
        call.BaseUrl.Should().Be("https://srv/api/geoassets/wms");
        call.Options.Layers.Should().Be("geoassets:feature");
        call.Options.Format.Should().Be("image/jpeg");
        _map.FeatureBatches.Should().BeEmpty();
    }

    [Fact]
    public async Task RenderAsync_ReadsTheEntrysOwnProvider_NotTheDecoratedSource()
    {
        // MapContainer reads the active entry through the decorated IAssetProvider; the tile layer's
        // parameters still come from the entry's own WMS provider.
        var entry = new ProviderEntry { Name = "WMS", Provider = new FakeWmsLayerProvider() };
        var request = new MapLayerRenderRequest(Div, entry, new ValidatingAssetProvider(entry.Provider));

        await Renderer().RenderAsync(request);

        _map.WmsAdds.Should().ContainSingle().Which.BaseUrl.Should().Be("https://wms.example.com/geoassets/wms");
    }

    [Fact]
    public async Task RenderAsync_AnEntryThatIsNotWms_Throws()
    {
        var entry = new ProviderEntry { Name = "Local", Provider = new FakeLayerProvider() };

        var act = () => Renderer().RenderAsync(MapLayerRenderRequest.For(Div, entry));

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*Local*");
        _map.WmsAdds.Should().BeEmpty();
    }

    [Fact]
    public async Task RemoveAsync_RemovesTheWmsLayerNamedAfterTheEntry()
    {
        var entry = new ProviderEntry { Name = "WMS", Provider = new FakeWmsLayerProvider() };

        await Renderer().RemoveAsync(MapLayerRenderRequest.For(Div, entry));

        _map.WmsRemoves.Should().Equal((Div, entry.Id.ToString()));
        _map.RemovedFeatures.Should().BeEmpty();
    }

    [Fact]
    public async Task RenderViewportAsync_DrawsNothing()
    {
        var entry = new ProviderEntry { Name = "WMS", Provider = new FakeWmsLayerProvider() };

        var rendered = await Renderer().RenderViewportAsync(MapLayerRenderRequest.For(Div, entry), new BoundingBox(-1, -1, 1, 1));

        rendered.Should().Be(0);
        _map.WmsAdds.Should().BeEmpty();
        _map.RawJsonBatches.Should().BeEmpty();
        _map.JsonBatches.Should().BeEmpty();
    }

    [Fact]
    public async Task RenderAndRemove_RejectNull()
    {
        var renderer = Renderer();

        await renderer.Invoking(r => r.RenderAsync(null!)).Should().ThrowAsync<ArgumentNullException>();
        await renderer.Invoking(r => r.RemoveAsync(null!)).Should().ThrowAsync<ArgumentNullException>();
    }

    private sealed class WmsDeclaringVector() : FakeLayerProvider(), IWmsProvider, IProviderCapabilities
    {
        public string WmsBaseUrl => "https://wms.example.com";
        public string WmsLayerName => "l";
        public string WmsFormat => "image/png";
        public ProviderRenderCapabilities RenderCapabilities => AssetProviderExtensions.DefaultVectorCapabilities;
    }
}
