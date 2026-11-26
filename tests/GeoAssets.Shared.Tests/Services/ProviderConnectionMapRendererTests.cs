using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Rendering;
using GeoAssets.Core.Services;
using GeoAssets.Shared.Interfaces;
using GeoAssets.Shared.Services;
using GeoAssets.Shared.Services.Rendering;
using GeoAssets.Shared.Tests.Services.Rendering;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GeoAssets.Shared.Tests.Services;

public class ProviderConnectionMapRendererTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private sealed class FakeMapContext(string mapDivId = "test-map") : ICurrentMapContext
    {
        public string MapDivId => mapDivId;
    }

    /// <summary>Draws synchronously, answering <paramref name="rendered"/>, or fails with <paramref name="failure"/>.</summary>
    private sealed class StubRenderer(int rendered, Exception? failure = null) : IProviderMapLayerRenderer
    {
        public List<MapLayerRenderRequest> Rendered { get; } = [];
        public bool ClearedWithFeatures => true;
        public bool CanRender(IAssetProvider provider) => true;

        public Task<int> RenderAsync(MapLayerRenderRequest request, CancellationToken cancellationToken = default)
        {
            if (failure is not null) throw failure;
            Rendered.Add(request);
            return Task.FromResult(rendered);
        }

        public Task RemoveAsync(MapLayerRenderRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int> RenderViewportAsync(MapLayerRenderRequest request, BoundingBox viewport, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private static ProviderMapLayerRendererChain RealChain(IMapInterop map) =>
        new([new WmsMapLayerRenderer(map), new VectorFeatureMapLayerRenderer(map, new FeatureRenderPipeline())]);

    private static ProviderConnectionMapRenderer Sut(
        IProviderPool pool, IMapInterop map, ProviderMapLayerRendererChain chain, string divId = "my-map") =>
        new(pool, map, new FakeMapContext(divId), chain, NullLogger<ProviderConnectionMapRenderer>.Instance);

    private static GeoFeature NewFeature() => new() { Id = Guid.NewGuid().ToString() };

    [Fact]
    public void Constructor_Subscribes_WmsEntryAdded_AddsWmsLayer()
    {
        // XD01-83 regression: fails without the fix — the pool has no EntryAdded event to
        // subscribe to, or this renderer never subscribes in its constructor.
        var pool = new ProviderPool();
        var map = new LayerRecordingMapInterop();
        using var sut = Sut(pool, map, RealChain(map));

        var entry = pool.Add("WMS Layer", new FakeWmsLayerProvider());

        map.WmsAdds.Should().ContainSingle();
        var call = map.WmsAdds[0];
        call.DivId.Should().Be("my-map");
        call.LayerId.Should().Be(entry.Id.ToString());
        call.BaseUrl.Should().Be("https://wms.example.com/geoassets/wms");
        call.Options.Layers.Should().Be("geoassets:layer");
        call.Options.Format.Should().Be("image/png");
        map.FeatureBatches.Should().BeEmpty();
        map.FitCalls.Should().BeEmpty("a tile layer draws no discrete features to fit the view to");
    }

    [Fact]
    public void EntryAdded_DrawsTheEntryThroughItsRenderer_OnTheCurrentMap()
    {
        var pool = new ProviderPool();
        var map = new LayerRecordingMapInterop();
        var renderer = new StubRenderer(rendered: 2);
        using var sut = Sut(pool, map, new ProviderMapLayerRendererChain([renderer]));

        var entry = pool.Add("Local", new FakeLayerProvider(NewFeature(), NewFeature()));

        renderer.Rendered.Should().ContainSingle();
        renderer.Rendered[0].MapDivId.Should().Be("my-map");
        renderer.Rendered[0].Entry.Should().BeSameAs(entry);
        renderer.Rendered[0].Source.Should().BeSameAs(entry.Provider);
        // Neither feature carries a geometry, so there's nothing to fit a view to.
        map.FitCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task EntryAdded_VectorFeaturesWithGeometry_RenderedAndMapFitToTheirUnionBoundingBox()
    {
        // A newly-connected provider's features render wherever they geographically are, which is
        // very often nowhere near the map's current (e.g. default startup) view — they'd be drawn
        // but invisible until the view is moved to them. This locks in that the renderer fits the
        // map to the imported data instead of leaving that to chance.
        var pool = new ProviderPool();
        var map = new LayerRecordingMapInterop();
        using var sut = Sut(pool, map, RealChain(map));

        var f1 = new GeoFeature { Id = Guid.NewGuid().ToString(), Geometry = new GeoPoint(-51.2, -30.0) };
        var f2 = new GeoFeature { Id = Guid.NewGuid().ToString(), Geometry = new GeoPoint(-50.3, -29.5) };

        pool.Add("Shapefile", new FakeLayerProvider(f1, f2));
        await map.Fitted.WaitAsync(Patience);

        map.RenderedFeatureIds.Should().BeEquivalentTo([f1.Id, f2.Id]);
        map.FeatureBatches.Should().OnlyContain(b => b.DivId == "my-map");
        map.WmsAdds.Should().BeEmpty();
        map.FitCalls.Should().ContainSingle();
        var call = map.FitCalls[0];
        call.DivId.Should().Be("my-map");
        call.Bbox.Should().BeEquivalentTo([-51.2, -30.0, -50.3, -29.5]);
    }

    [Fact]
    public void EntryAdded_NoRendererDrawsIt_TouchesNothing()
    {
        var pool = new ProviderPool();
        var map = new LayerRecordingMapInterop();
        using var sut = Sut(pool, map, RealChain(map));

        var act = () => pool.Add("Nothing", new CapabilityDeclaringLayerProvider(ProviderRenderCapabilities.None, NewFeature()));

        act.Should().NotThrow();
        map.WmsAdds.Should().BeEmpty();
        map.FeatureBatches.Should().BeEmpty();
        map.FitCalls.Should().BeEmpty();
    }

    [Fact]
    public void Dispose_UnsubscribesFromEntryAdded()
    {
        var pool = new ProviderPool();
        var map = new LayerRecordingMapInterop();
        var sut = Sut(pool, map, RealChain(map));

        sut.Dispose();
        pool.Add("WMS Layer", new FakeWmsLayerProvider());

        map.WmsAdds.Should().BeEmpty();
    }

    [Fact]
    public void EntryAdded_RenderThrows_ExceptionIsCaughtAndDoesNotPropagateToAdd()
    {
        var pool = new ProviderPool();
        var map = new LayerRecordingMapInterop();
        using var sut = Sut(pool, map, new ProviderMapLayerRendererChain(
            [new StubRenderer(0, new InvalidOperationException("Simulated JS interop failure."))]));

        var act = () => pool.Add("Local", new FakeLayerProvider(NewFeature()));

        act.Should().NotThrow();
        map.FitCalls.Should().BeEmpty();
    }
}
