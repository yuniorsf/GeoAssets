using System.Text.Json;
using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Providers;
using GeoAssets.Core.Rendering;
using GeoAssets.Core.Services;
using GeoAssets.Shared.Services.Rendering;
using Xunit;

namespace GeoAssets.Shared.Tests.Services.Rendering;

/// <summary>
/// XD01-422 — <see cref="VectorFeatureMapLayerRenderer"/>: a whole layer streams through
/// <see cref="FeatureRenderPipeline"/> in <see cref="VectorFeatureMapLayerRenderer.ChunkSize"/> chunks, removal
/// takes each feature off, and a viewport prefers the raw-JSON chunks and falls back to the JSON elements — the
/// paths <c>MapContainer</c> and <c>ProviderPoolPanel</c> ran inline before.
/// </summary>
public sealed class VectorFeatureMapLayerRendererTests
{
    private const string Div = "geoassets-map";
    private static readonly ViewportQuery Viewport = new(new BoundingBox(-38.6, -13.05, -38.4, -12.8), 14);

    private readonly LayerRecordingMapInterop _map = new();

    private VectorFeatureMapLayerRenderer Renderer() => new(_map, new FeatureRenderPipeline());

    private static GeoFeature[] Features(int count) =>
        [.. Enumerable.Range(0, count).Select(i => new GeoFeature { Id = $"f-{i}" })];

    private static MapLayerRenderRequest Request(IAssetProvider provider) =>
        MapLayerRenderRequest.For(Div, new ProviderEntry { Name = "Local", Provider = provider });

    [Fact]
    public void CanRender_AProviderWithoutDeclaredCapabilities()
    {
        Renderer().CanRender(new FakeLayerProvider()).Should().BeTrue("an undeclared provider is read as vector features");
    }

    [Fact]
    public void CanRender_AVectorProviderBehindTheDecoratorChain()
    {
        Renderer().CanRender(new ValidatingAssetProvider(new FakeLayerProvider())).Should().BeTrue();
    }

    [Fact]
    public void CannotRender_AWmsProvider()
    {
        Renderer().CanRender(new FakeWmsLayerProvider()).Should().BeFalse();
    }

    [Fact]
    public void CannotRender_AProviderDeclaringNoVectorFeatures()
    {
        Renderer().CanRender(new CapabilityDeclaringLayerProvider(ProviderRenderCapabilities.VectorTiles)).Should().BeFalse();
    }

    [Fact]
    public void CanRender_RejectsNull()
    {
        var act = () => Renderer().CanRender(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void IsClearedWithFeatures()
    {
        Renderer().ClearedWithFeatures.Should().BeTrue();
    }

    [Fact]
    public async Task RenderAsync_StreamsEveryFeatureInChunks_AndCountsThem()
    {
        var features = Features(VectorFeatureMapLayerRenderer.ChunkSize + 2);

        var rendered = await Renderer().RenderAsync(Request(new FakeLayerProvider(features)));

        rendered.Should().Be(features.Length);
        _map.FeatureBatches.Select(b => b.Features.Count).Should().Equal(VectorFeatureMapLayerRenderer.ChunkSize, 2);
        _map.FeatureBatches.Should().OnlyContain(b => b.DivId == Div);
        _map.RenderedFeatureIds.Should().Equal(features.Select(f => f.Id));
        _map.WmsAdds.Should().BeEmpty();
    }

    [Fact]
    public async Task RenderAsync_ReadsTheRequestSource()
    {
        // MapContainer reads the active entry through the decorated provider: the source is what's read.
        var entry = new ProviderEntry { Name = "Local", Provider = new FakeLayerProvider(Features(1)) };
        var source = new FakeLayerProvider(Features(3));

        var rendered = await Renderer().RenderAsync(new MapLayerRenderRequest(Div, entry, source));

        rendered.Should().Be(3);
    }

    [Fact]
    public async Task RenderAsync_AnEmptyProvider_DrawsNothing()
    {
        var rendered = await Renderer().RenderAsync(Request(new FakeLayerProvider()));

        rendered.Should().Be(0);
        _map.FeatureBatches.Should().BeEmpty();
    }

    [Fact]
    public async Task RenderAsync_Cancelled_Throws()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => Renderer().RenderAsync(Request(new FakeLayerProvider(Features(3))), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task RemoveAsync_RemovesEveryFeature()
    {
        var features = Features(3);

        await Renderer().RemoveAsync(Request(new FakeLayerProvider(features)));

        _map.RemovedFeatures.Should().Equal(features.Select(f => (Div, f.Id)));
        _map.WmsRemoves.Should().BeEmpty();
    }

    [Fact]
    public async Task RenderViewportAsync_RawJsonSupported_RendersTheChunks()
    {
        var provider = new FakeLayerProvider { RawJson = "[]" };

        var rendered = await Renderer().RenderViewportAsync(Request(provider), Viewport);

        rendered.Should().Be(1, "even an empty bbox response is one non-null chunk");
        _map.RawJsonBatches.Should().Equal((Div, "[]"));
        _map.JsonBatches.Should().BeEmpty();
        provider.LastBounds.Should().Be((Viewport.Bounds.MinLon, Viewport.Bounds.MinLat, Viewport.Bounds.MaxLon, Viewport.Bounds.MaxLat));
    }

    [Fact]
    public async Task RenderViewportAsync_NoRawJson_FallsBackToJsonElements()
    {
        using var doc = JsonDocument.Parse("""[{"id":"a"},{"id":"b"}]""");
        var json = doc.RootElement.EnumerateArray().Select(e => e.Clone()).ToList();
        var provider = new FakeLayerProvider { Json = json };

        var rendered = await Renderer().RenderViewportAsync(Request(provider), Viewport);

        rendered.Should().Be(2);
        _map.RawJsonBatches.Should().BeEmpty();
        _map.JsonBatches.Should().ContainSingle().Which.Features.Should().HaveCount(2);
        provider.LastBounds.Should().Be((Viewport.Bounds.MinLon, Viewport.Bounds.MinLat, Viewport.Bounds.MaxLon, Viewport.Bounds.MaxLat));
    }

    [Fact]
    public async Task RenderViewportAsync_SupersededDuringTheFallback_PaintsNothing()
    {
        using var cts = new CancellationTokenSource();
        using var doc = JsonDocument.Parse("""[{"id":"a"}]""");
        var provider = new FakeLayerProvider
        {
            Json = [.. doc.RootElement.EnumerateArray().Select(e => e.Clone())],
            WhileFetchingJson = cts.CancelAsync,
        };

        var rendered = await Renderer().RenderViewportAsync(Request(provider), Viewport, cts.Token);

        rendered.Should().Be(0);
        _map.JsonBatches.Should().BeEmpty("a superseded fetch must not paint its stale result");
    }

    [Fact]
    public async Task RenderViewportAsync_PassesTheViewportAndItsTokenToTheProvider()
    {
        // XD01-429: the zoom reaches the provider (REST forwards it), and so does the token that aborts the fetch.
        using var cts = new CancellationTokenSource();
        var provider = new ViewportAwareLayerProvider(["[]"]);

        await Renderer().RenderViewportAsync(Request(provider), Viewport, cts.Token);

        provider.ChunkViewport.Should().Be(Viewport);
        provider.ChunkToken.Should().Be(cts.Token);
    }

    [Fact]
    public async Task RenderViewportAsync_FallbackReadsTheViewportWithItsToken()
    {
        using var cts = new CancellationTokenSource();
        var provider = new ViewportAwareLayerProvider([]);

        await Renderer().RenderViewportAsync(Request(provider), Viewport, cts.Token);

        provider.JsonViewport.Should().Be(Viewport);
        provider.JsonToken.Should().Be(cts.Token);
    }

    [Fact]
    public async Task RenderViewportAsync_ChunkArrivingAfterASupersedingPan_IsNotPainted()
    {
        // XD01-429: the first chunk's render is where a newer pan lands; the second chunk is already
        // fetched, but must not be painted over the newer viewport.
        using var cts = new CancellationTokenSource();
        var map = new LayerRecordingMapInterop { WhileRenderingRawJson = cts.CancelAsync };
        var provider = new ViewportAwareLayerProvider(["[1]", "[2]"]);

        var act = () => new VectorFeatureMapLayerRenderer(map, new FeatureRenderPipeline())
            .RenderViewportAsync(Request(provider), Viewport, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        map.RawJsonBatches.Should().Equal((Div, "[1]"));
    }

    [Fact]
    public async Task RenderViewportAsync_DefaultViewportOverloads_ForwardTheBoundsToTheBboxReads()
    {
        // A provider that knows nothing of ViewportQuery is read through IAssetProvider's defaults.
        var provider = new FakeLayerProvider { RawJson = "[]" };

        await Renderer().RenderViewportAsync(Request(provider), Viewport);

        provider.LastBounds.Should().Be((Viewport.Bounds.MinLon, Viewport.Bounds.MinLat, Viewport.Bounds.MaxLon, Viewport.Bounds.MaxLat));
    }

    [Fact]
    public async Task Requests_RejectNull()
    {
        var renderer = Renderer();

        await renderer.Invoking(r => r.RenderAsync(null!)).Should().ThrowAsync<ArgumentNullException>();
        await renderer.Invoking(r => r.RemoveAsync(null!)).Should().ThrowAsync<ArgumentNullException>();
        await renderer.Invoking(r => r.RenderViewportAsync(null!, Viewport)).Should().ThrowAsync<ArgumentNullException>();
    }
}
