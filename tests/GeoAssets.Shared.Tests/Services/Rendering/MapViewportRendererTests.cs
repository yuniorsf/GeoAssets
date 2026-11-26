using System.Text.Json;
using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Providers;
using GeoAssets.Core.Rendering;
using GeoAssets.Core.Services;
using GeoAssets.Shared.Services.Rendering;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GeoAssets.Shared.Tests.Services.Rendering;

/// <summary>
/// XD01-429 — <see cref="MapViewportRenderer"/>: a viewport is fetched once it stays put for
/// <see cref="MapViewportRenderer.DebounceInterval"/> (on an injected clock, never a real delay), padded and with its
/// zoom; a newer viewport supersedes the pass before it — its fetch's token is cancelled and its late result is never
/// drawn — and only a completed pass ends, i.e. removes what it did not draw.
/// </summary>
public sealed class MapViewportRendererTests
{
    private const string Div = "geoassets-map";
    private static readonly ViewportQuery Viewport = new(new BoundingBox(-40, -20, -38, -18), 12);

    private readonly LayerRecordingMapInterop _map = new();
    private readonly FakeTimeProvider _clock = new();

    private MapViewportRenderer Sut() => new(
        _map,
        new ProviderMapLayerRendererChain([new WmsMapLayerRenderer(_map), new VectorFeatureMapLayerRenderer(_map, new FeatureRenderPipeline())]),
        _clock);

    private static MapLayerRenderRequest Request(IAssetProvider provider) =>
        MapLayerRenderRequest.For(Div, new ProviderEntry { Name = "Entry", Provider = provider });

    private static IReadOnlyList<JsonElement> Elements(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return [.. doc.RootElement.EnumerateArray().Select(e => e.Clone())];
    }

    [Fact]
    public async Task Debounced_FetchesNothingUntilTheIntervalElapses()
    {
        var provider = new FakeLayerProvider { RawJson = "[]" };
        using var sut = Sut();

        var pass = sut.RenderDebouncedAsync(Div, Viewport, () => [Request(provider)]);
        _clock.Advance(MapViewportRenderer.DebounceInterval - TimeSpan.FromMilliseconds(1));

        pass.IsCompleted.Should().BeFalse();
        provider.LastBounds.Should().BeNull("nothing is fetched while the view may still move");
        _map.ViewportPasses.Should().BeEmpty();

        _clock.Advance(TimeSpan.FromMilliseconds(1));
        var outcome = await pass;

        outcome.Should().NotBeNull();
        outcome!.Value.Count.Should().Be(1);
        _map.ViewportPasses.Should().Equal(("begin", Div, 1), ("end", Div, 1));
        _map.RawJsonBatches.Should().Equal((Div, "[]"));
    }

    [Fact]
    public async Task Debounced_ANewerViewportWithinTheInterval_SupersedesTheOlder()
    {
        var older = new FakeLayerProvider { RawJson = "[\"older\"]" };
        var newer = new FakeLayerProvider { RawJson = "[\"newer\"]" };
        using var sut = Sut();

        var first = sut.RenderDebouncedAsync(Div, Viewport, () => [Request(older)]);
        _clock.Advance(TimeSpan.FromMilliseconds(100));
        var second = sut.RenderDebouncedAsync(Div, Viewport, () => [Request(newer)]);

        (await first).Should().BeNull("a superseded viewport draws nothing");
        _clock.Advance(TimeSpan.FromMilliseconds(100));
        second.IsCompleted.Should().BeFalse("the interval restarts with the newer viewport");
        _clock.Advance(TimeSpan.FromMilliseconds(50));
        (await second).Should().NotBeNull();

        older.LastBounds.Should().BeNull();
        _map.RawJsonBatches.Should().Equal((Div, "[\"newer\"]"));
        _map.ViewportPasses.Should().Equal(("begin", Div, 1), ("end", Div, 1));
    }

    [Fact]
    public async Task TheFetch_IsPaddedAndCarriesTheZoom()
    {
        var provider = new ViewportAwareLayerProvider(["[]"]);
        using var sut = Sut();

        await sut.RenderNowAsync(Div, Viewport, () => [Request(provider)]);

        provider.ChunkViewport.Should().Be(new ViewportQuery(new BoundingBox(-40.5, -20.5, -37.5, -17.5), 12),
            "a quarter of the 2° view on every side, at the view's zoom");
    }

    [Fact]
    public async Task Now_DoesNotWait()
    {
        var provider = new FakeLayerProvider { RawJson = "[]" };
        using var sut = Sut();

        var outcome = await sut.RenderNowAsync(Div, Viewport, () => [Request(provider)]);

        outcome.Should().NotBeNull();
        _map.ViewportPasses.Should().Equal(("begin", Div, 1), ("end", Div, 1));
    }

    [Fact]
    public async Task ANewerViewportDuringTheFetch_CancelsIt_AndTheOlderPassNeitherPaintsNorEnds()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var older = new ViewportAwareLayerProvider([]);
        var slow = new FakeLayerProvider { Json = Elements("""[{"id":"stale"}]"""), WhileFetchingJson = () => gate.Task };
        var newer = new FakeLayerProvider { Json = Elements("""[{"id":"fresh"}]""") };
        using var sut = Sut();

        var first = sut.RenderNowAsync(Div, Viewport, () => [Request(older), Request(slow)]);
        older.JsonToken.IsCancellationRequested.Should().BeFalse();
        var second = sut.RenderNowAsync(Div, Viewport, () => [Request(newer)]);

        older.JsonToken.IsCancellationRequested.Should().BeTrue("the superseded pass's fetches are cancelled");
        (await second).Should().NotBeNull();
        gate.SetResult();
        (await first).Should().BeNull();

        _map.JsonBatches.SelectMany(b => b.Features).Select(f => f.GetProperty("id").GetString())
            .Should().Equal(["fresh"], "the older pass's late result is never drawn");
        _map.ViewportPasses.Should().Equal(("begin", Div, 1), ("begin", Div, 2), ("end", Div, 2));
    }

    [Fact]
    public async Task TheEntries_AreReadOnceTheDebounceIsOver()
    {
        var provider = new FakeLayerProvider { RawJson = "[]" };
        var entries = new List<MapLayerRenderRequest>();
        using var sut = Sut();

        var pass = sut.RenderDebouncedAsync(Div, Viewport, () => entries);
        entries.Add(Request(provider));
        _clock.Advance(MapViewportRenderer.DebounceInterval);
        await pass;

        provider.LastBounds.Should().NotBeNull("an entry opened during the debounce is drawn");
    }

    [Fact]
    public async Task AnEntryNoRendererDraws_IsSkipped_AndAWmsEntryDrawsNothing()
    {
        var undrawable = new CapabilityDeclaringLayerProvider(ProviderRenderCapabilities.None);
        var wms = new FakeWmsLayerProvider();
        using var sut = Sut();

        var outcome = await sut.RenderNowAsync(Div, Viewport, () => [Request(undrawable), Request(wms)]);

        outcome!.Value.Count.Should().Be(0);
        _map.WmsAdds.Should().BeEmpty();
        _map.WmsRemoves.Should().BeEmpty("the tile layer fetches its own tiles on pan");
        _map.ViewportPasses.Should().Equal(("begin", Div, 1), ("end", Div, 1));
    }

    [Fact]
    public async Task Cancel_SupersedesThePendingPass()
    {
        var provider = new FakeLayerProvider { RawJson = "[]" };
        using var sut = Sut();

        var pass = sut.RenderDebouncedAsync(Div, Viewport, () => [Request(provider)]);
        sut.Cancel();
        _clock.Advance(MapViewportRenderer.DebounceInterval);

        (await pass).Should().BeNull();
        provider.LastBounds.Should().BeNull();
        _map.ViewportPasses.Should().BeEmpty();
    }

    [Fact]
    public async Task Dispose_SupersedesThePendingPass()
    {
        var sut = Sut();
        var pass = sut.RenderDebouncedAsync(Div, Viewport, () => [Request(new FakeLayerProvider { RawJson = "[]" })]);

        sut.Dispose();

        (await pass).Should().BeNull();
        sut.Cancel(); // nothing left to cancel
    }

    [Fact]
    public async Task NullRequests_AreRejected()
    {
        using var sut = Sut();

        await sut.Invoking(s => s.RenderNowAsync(Div, Viewport, null!)).Should().ThrowAsync<ArgumentNullException>();
    }
}
