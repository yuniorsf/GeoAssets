using FluentAssertions;
using GeoAssets.Core.Rendering;
using GeoAssets.Shared.Services;
using GeoAssets.Shared.Services.MapTiles;
using GeoAssets.Shared.Tests.Services.Rendering;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;
using Xunit;

namespace GeoAssets.Shared.Tests.Services;

/// <summary>
/// XD01-465 — <see cref="MapInteropService"/> as <see cref="Interfaces.IMapLocalTileLayerInterop"/>: a local raster layer
/// reaches <c>GeoAssets.addLocalRasterLayer</c> with a reference to its <see cref="LocalRasterTileReader"/> and the pack's
/// media type, zooms and bounds; the reference is kept while the layer is on the map and released when it is removed,
/// when its map is destroyed, or with the service; MapLibre mode draws none. And the reader answers JS the tile images.
/// </summary>
public sealed class MapInteropServiceLocalRasterTests
{
    private const string Div = "map1";

    private static readonly LocalRasterTileLayer Layer = new(FakeLocalRasterLayerProvider.Layers, "image/webp", 2, 12, new BoundingBox(-9.5, 38.6, -9.0, 38.9));

    private sealed class RecordingJsRuntime : IJSRuntime
    {
        public List<(string Identifier, object?[] Args)> Calls { get; } = [];

        public Exception? Fail { get; set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            Calls.Add((identifier, args ?? []));
            return Fail is { } fail ? ValueTask.FromException<TValue>(fail) : ValueTask.FromResult(default(TValue)!);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            InvokeAsync<TValue>(identifier, args);
    }

    private static MapInteropService Sut(RecordingJsRuntime js, MapRenderMode mode = MapRenderMode.Leaflet) =>
        new(js, new TestAssetProvider(), Options.Create(new MapInteropOptions { RenderMode = mode }));

    private static DotNetObjectReference<LocalRasterTileReader> ReaderOf((string Identifier, object?[] Args) call) =>
        call.Args[2].Should().BeOfType<DotNetObjectReference<LocalRasterTileReader>>().Subject;

    [Fact]
    public async Task Adding_CallsLeaflet_WithTheReaderAndThePacksLayer()
    {
        var js = new RecordingJsRuntime();
        await using var sut = Sut(js);

        await sut.AddLocalRasterTileLayerAsync(Div, "layer-1", new StubRasterTiles(), Layer);

        var call = js.Calls.Should().ContainSingle().Subject;
        call.Identifier.Should().Be("GeoAssets.addLocalRasterLayer");
        call.Args[0].Should().Be(Div);
        call.Args[1].Should().Be("layer-1");
        ReaderOf(call).Value.Should().NotBeNull();
        call.Args[3].Should().BeEquivalentTo(new { contentType = "image/webp", minZoom = 2, maxZoom = 12, bounds = new[] { -9.5, 38.6, -9.0, 38.9 } });
    }

    [Fact]
    public async Task AddingAgain_ReusesTheLayersReader()
    {
        var js = new RecordingJsRuntime();
        await using var sut = Sut(js);

        await sut.AddLocalRasterTileLayerAsync(Div, "layer-1", new StubRasterTiles(), Layer);
        await sut.AddLocalRasterTileLayerAsync(Div, "layer-1", new StubRasterTiles(), Layer);

        js.Calls.Should().HaveCount(2, "JS adds the layer only when the map has none, e.g. after the map was rebuilt");
        ReaderOf(js.Calls[1]).Should().BeSameAs(ReaderOf(js.Calls[0]));
    }

    [Fact]
    public async Task Removing_CallsLeaflet_AndReleasesTheReader_OnlyForALayerItAdded()
    {
        var js = new RecordingJsRuntime();
        await using var sut = Sut(js);
        await sut.AddLocalRasterTileLayerAsync(Div, "layer-1", new StubRasterTiles(), Layer);
        var reader = ReaderOf(js.Calls[0]);

        await sut.RemoveLocalRasterTileLayerAsync(Div, "layer-1");
        await sut.RemoveLocalRasterTileLayerAsync(Div, "never-added");

        js.Calls.Select(c => c.Identifier).Should().Equal("GeoAssets.addLocalRasterLayer", "GeoAssets.removeLocalRasterLayer");
        js.Calls[1].Args.Should().Equal(Div, "layer-1");
        FluentActions.Invoking(() => reader.Value).Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public async Task DestroyingTheMap_ReleasesItsReaders_SoTheNextMapGetsANewOne()
    {
        var js = new RecordingJsRuntime();
        await using var sut = Sut(js);
        await sut.AddLocalRasterTileLayerAsync(Div, "layer-1", new StubRasterTiles(), Layer);
        await sut.AddLocalRasterTileLayerAsync("other-map", "layer-1", new StubRasterTiles(), Layer);
        var first = ReaderOf(js.Calls[0]);
        var other = ReaderOf(js.Calls[1]);

        await sut.DestroyMapAsync(Div);
        await sut.AddLocalRasterTileLayerAsync(Div, "layer-1", new StubRasterTiles(), Layer);

        FluentActions.Invoking(() => first.Value).Should().Throw<ObjectDisposedException>();
        other.Value.Should().NotBeNull("another map's layer is untouched");
        ReaderOf(js.Calls[^1]).Should().NotBeSameAs(first);
    }

    [Fact]
    public async Task DisposingTheService_ReleasesEveryReader()
    {
        var js = new RecordingJsRuntime();
        var sut = Sut(js);
        await sut.AddLocalRasterTileLayerAsync(Div, "layer-1", new StubRasterTiles(), Layer);

        await sut.DisposeAsync();

        FluentActions.Invoking(() => ReaderOf(js.Calls[0]).Value).Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public async Task AFailedAdd_ReleasesTheNewReader_AndTheNextAddTriesAgain()
    {
        var js = new RecordingJsRuntime { Fail = new JSException("no map") };
        await using var sut = Sut(js);

        await sut.Invoking(s => s.AddLocalRasterTileLayerAsync(Div, "layer-1", new StubRasterTiles(), Layer)).Should().ThrowAsync<JSException>();
        js.Fail = null;
        await sut.AddLocalRasterTileLayerAsync(Div, "layer-1", new StubRasterTiles(), Layer);

        FluentActions.Invoking(() => ReaderOf(js.Calls[0]).Value).Should().Throw<ObjectDisposedException>();
        ReaderOf(js.Calls[1]).Value.Should().NotBeNull();
    }

    [Fact]
    public async Task ALayerOfUnknownBounds_IsSentWithoutBounds()
    {
        var js = new RecordingJsRuntime();
        await using var sut = Sut(js);

        await sut.AddLocalRasterTileLayerAsync(Div, "layer-1", new StubRasterTiles(), new LocalRasterTileLayer(FakeLocalRasterLayerProvider.Layers, "image/png", 0, 5));

        js.Calls[0].Args[3].Should().BeEquivalentTo(new { contentType = "image/png", minZoom = 0, maxZoom = 5, bounds = (double[]?)null });
    }

    [Fact]
    public async Task MapLibreMode_DrawsNoLocalRasterLayer()
    {
        var js = new RecordingJsRuntime();
        await using var sut = Sut(js, MapRenderMode.MapLibre);

        await sut.AddLocalRasterTileLayerAsync(Div, "layer-1", new StubRasterTiles(), Layer);
        await sut.RemoveLocalRasterTileLayerAsync(Div, "layer-1");

        js.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Adding_RejectsNull()
    {
        await using var sut = Sut(new RecordingJsRuntime());

        await sut.Invoking(s => s.AddLocalRasterTileLayerAsync(Div, "l", null!, Layer)).Should().ThrowAsync<ArgumentNullException>();
        await sut.Invoking(s => s.AddLocalRasterTileLayerAsync(Div, "l", new StubRasterTiles(), null!)).Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task TheReader_AnswersATilesImage_AndNothingElsewhere()
    {
        var tiles = new StubRasterTiles();
        tiles.Images[new TileAddress(4, 7, 6)] = [1, 2, 3];
        var reader = new LocalRasterTileReader(tiles, Layer);

        (await reader.GetTileFromJsAsync(4, 7, 6)).Should().Equal(1, 2, 3);
        (await reader.GetTileFromJsAsync(4, 0, 0)).Should().BeNull("the source has no tile there");
        tiles.Requests.Should().HaveCount(2);
        tiles.Requests[0].LayerSet.Should().BeSameAs(FakeLocalRasterLayerProvider.Layers);

        (await reader.GetTileFromJsAsync(1, 0, 0)).Should().BeNull("below the layer's lowest zoom");
        (await reader.GetTileFromJsAsync(13, 0, 0)).Should().BeNull("above its highest");
        (await reader.GetTileFromJsAsync(4, 16, 0)).Should().BeNull("outside the pyramid");
        (await reader.GetTileFromJsAsync(4, 0, -1)).Should().BeNull();
        tiles.Requests.Should().HaveCount(2, "a tile outside the layer is never asked of the source");
    }
}
