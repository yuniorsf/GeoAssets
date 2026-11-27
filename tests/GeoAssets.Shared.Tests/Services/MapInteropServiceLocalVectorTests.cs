using System.IO.Compression;
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
/// XD01-460 — <see cref="MapInteropService"/> as <see cref="Interfaces.IMapLocalVectorTileLayerInterop"/>: in MapLibre
/// mode a local vector layer reaches <c>GeoAssetsMapLibre.addLocalVectorLayer</c> with a reference to its
/// <see cref="LocalVectorTileReader"/> and the pack's zooms, bounds, MVT layers and colours; the reference is kept while
/// the layer is on the map and released when it is removed, when its map is destroyed, or with the service; a Leaflet map
/// draws none. And the reader answers JS each tile uncompressed, nothing outside the layer.
/// </summary>
public sealed class MapInteropServiceLocalVectorTests
{
    private const string Div = "map1";

    private static readonly LocalVectorTileLayer Layer = new(FakeLocalVectorLayerProvider.Layers, ["poles", "lines"], 2, 12, new BoundingBox(-9.5, 38.6, -9.0, 38.9));

    private sealed class RecordingJsRuntime : IJSRuntime
    {
        public List<(string Identifier, object?[] Args)> Calls { get; } = [];

        public Exception? Fail { get; set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            Calls.Add((identifier, args ?? []));
            return Fail is { } fail && !string.Equals(identifier, "import", StringComparison.Ordinal) ? ValueTask.FromException<TValue>(fail) : ValueTask.FromResult(default(TValue)!);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            InvokeAsync<TValue>(identifier, args);
    }

    private static MapInteropService Sut(RecordingJsRuntime js, MapRenderMode mode = MapRenderMode.MapLibre) =>
        new(js, new TestAssetProvider(), Options.Create(new MapInteropOptions { RenderMode = mode }));

    private static DotNetObjectReference<LocalVectorTileReader> ReaderOf((string Identifier, object?[] Args) call) =>
        call.Args[2].Should().BeOfType<DotNetObjectReference<LocalVectorTileReader>>().Subject;

    private static (string Identifier, object?[] Args) Added(RecordingJsRuntime js, int n = 0) =>
        js.Calls.Where(c => string.Equals(c.Identifier, "GeoAssetsMapLibre.addLocalVectorLayer", StringComparison.Ordinal)).ElementAt(n);

    [Fact]
    public async Task Adding_CallsMapLibre_WithTheReaderAndThePacksLayer()
    {
        var js = new RecordingJsRuntime();
        await using var sut = Sut(js);

        await sut.AddLocalVectorTileLayerAsync(Div, "layer-1", new StubVectorTiles(), Layer);

        var call = Added(js);
        call.Args[0].Should().Be(Div);
        call.Args[1].Should().Be("layer-1");
        ReaderOf(call).Value.Should().NotBeNull();
        call.Args[3].Should().BeEquivalentTo(new
        {
            minZoom = 2,
            maxZoom = 12,
            bounds = new[] { -9.5, 38.6, -9.0, 38.9 },
            sourceLayers = new[] { "poles", "lines" },
            colors = new { point = "#e74c3c", line = "#3498db", polygon = "#2ecc71" },
        });
    }

    [Fact]
    public async Task AddingAgain_ReusesTheReader_AndRemovingReleasesIt_OnlyForALayerItAdded()
    {
        var js = new RecordingJsRuntime();
        await using var sut = Sut(js);

        await sut.AddLocalVectorTileLayerAsync(Div, "layer-1", new StubVectorTiles(), Layer);
        await sut.AddLocalVectorTileLayerAsync(Div, "layer-1", new StubVectorTiles(), Layer);
        var reader = ReaderOf(Added(js));
        ReaderOf(Added(js, 1)).Should().BeSameAs(reader);

        await sut.RemoveLocalVectorTileLayerAsync(Div, "layer-1");
        await sut.RemoveLocalVectorTileLayerAsync(Div, "never-added");

        js.Calls.Count(c => string.Equals(c.Identifier, "GeoAssetsMapLibre.removeLocalVectorLayer", StringComparison.Ordinal)).Should().Be(1);
        FluentActions.Invoking(() => reader.Value).Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public async Task DestroyingTheMap_OrDisposingTheService_ReleasesItsReaders()
    {
        var js = new RecordingJsRuntime();
        var sut = Sut(js);
        await sut.AddLocalVectorTileLayerAsync(Div, "layer-1", new StubVectorTiles(), Layer);
        await sut.AddLocalVectorTileLayerAsync("other-map", "layer-1", new StubVectorTiles(), Layer);
        var first = ReaderOf(Added(js));
        var other = ReaderOf(Added(js, 1));

        await sut.DestroyMapAsync(Div);

        FluentActions.Invoking(() => first.Value).Should().Throw<ObjectDisposedException>();
        other.Value.Should().NotBeNull("another map's layer is untouched");

        await sut.DisposeAsync();

        FluentActions.Invoking(() => other.Value).Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public async Task AFailedAdd_ReleasesTheNewReader_AndALayerOfUnknownBoundsIsSentWithoutBounds()
    {
        var js = new RecordingJsRuntime { Fail = new JSException("no map") };
        await using var sut = Sut(js);

        await sut.Invoking(s => s.AddLocalVectorTileLayerAsync(Div, "layer-1", new StubVectorTiles(), Layer)).Should().ThrowAsync<JSException>();
        js.Fail = null;
        await sut.AddLocalVectorTileLayerAsync(Div, "layer-1", new StubVectorTiles(), new LocalVectorTileLayer(FakeLocalVectorLayerProvider.Layers, [], 0, 5));

        FluentActions.Invoking(() => ReaderOf(Added(js)).Value).Should().Throw<ObjectDisposedException>();
        Added(js, 1).Args[3].Should().BeEquivalentTo(new { minZoom = 0, maxZoom = 5, bounds = (double[]?)null, sourceLayers = Array.Empty<string>() });
    }

    [Fact]
    public async Task ALeafletMap_DrawsNoLocalVectorLayer()
    {
        var js = new RecordingJsRuntime();
        await using var sut = Sut(js, MapRenderMode.Leaflet);

        await sut.AddLocalVectorTileLayerAsync(Div, "layer-1", new StubVectorTiles(), Layer);
        await sut.RemoveLocalVectorTileLayerAsync(Div, "layer-1");

        js.Calls.Should().BeEmpty("Leaflet can't draw vector tiles; the pack's features are drawn instead");
    }

    [Fact]
    public async Task Adding_RejectsNull()
    {
        await using var sut = Sut(new RecordingJsRuntime());

        await sut.Invoking(s => s.AddLocalVectorTileLayerAsync(Div, "l", null!, Layer)).Should().ThrowAsync<ArgumentNullException>();
        await sut.Invoking(s => s.AddLocalVectorTileLayerAsync(Div, "l", new StubVectorTiles(), null!)).Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task TheReader_AnswersATileUncompressed_AndNothingOutsideTheLayer()
    {
        var tiles = new StubVectorTiles();
        tiles.Tiles[new TileAddress(4, 7, 6)] = new TilePayload(new byte[] { 0x1A, 2, 3 }, TileFormats.MapboxVectorTileContentType);
        tiles.Tiles[new TileAddress(4, 7, 7)] = new TilePayload(Gzip([0x1A, 9, 9, 9]), TileFormats.MapboxVectorTileContentType, "gzip");
        var reader = new LocalVectorTileReader(tiles, Layer);

        (await reader.GetTileFromJsAsync(4, 7, 6)).Should().Equal(0x1A, 2, 3);
        (await reader.GetTileFromJsAsync(4, 7, 7)).Should().Equal(new byte[] { 0x1A, 9, 9, 9 }, "MapLibre inflates nothing a protocol answers");
        (await reader.GetTileFromJsAsync(4, 0, 0)).Should().BeNull("the source has no tile there");
        tiles.Requests.Should().HaveCount(3);
        tiles.Requests[0].LayerSet.Should().BeSameAs(FakeLocalVectorLayerProvider.Layers);

        (await reader.GetTileFromJsAsync(1, 0, 0)).Should().BeNull("below the layer's lowest zoom");
        (await reader.GetTileFromJsAsync(13, 0, 0)).Should().BeNull("above its highest: MapLibre overzooms the highest");
        (await reader.GetTileFromJsAsync(4, 16, 0)).Should().BeNull("outside the pyramid");
        (await reader.GetTileFromJsAsync(4, 0, -1)).Should().BeNull();
        tiles.Requests.Should().HaveCount(3, "a tile outside the layer is never asked of the source");
    }

    [Fact]
    public async Task TheReader_RefusesAnUnknownEncoding_AndATileInflatingPastItsBound()
    {
        var tiles = new StubVectorTiles();
        tiles.Tiles[new TileAddress(4, 1, 1)] = new TilePayload(new byte[] { 1 }, TileFormats.MapboxVectorTileContentType, "zstd");
        tiles.Tiles[new TileAddress(4, 1, 2)] = new TilePayload(Gzip(new byte[LocalVectorTileReader.MaxTileBytes + 1]), TileFormats.MapboxVectorTileContentType, "gzip");
        var reader = new LocalVectorTileReader(tiles, Layer);

        await reader.Invoking(r => r.GetTileFromJsAsync(4, 1, 1)).Should().ThrowAsync<InvalidDataException>().WithMessage("*zstd*");
        await reader.Invoking(r => r.GetTileFromJsAsync(4, 1, 2)).Should().ThrowAsync<InvalidDataException>();
    }

    private static byte[] Gzip(byte[] content)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
            gzip.Write(content);
        return output.ToArray();
    }
}
