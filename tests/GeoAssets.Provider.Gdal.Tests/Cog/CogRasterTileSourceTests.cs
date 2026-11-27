using FluentAssertions;
using GeoAssets.Core.Rendering;
using GeoAssets.Provider.Gdal.Cog;
using Xunit;

namespace GeoAssets.Provider.Gdal.Tests.Cog;

/// <summary>
/// <see cref="CogRasterTileSource"/> against Cloud Optimized GeoTIFFs GDAL writes in each test (EPSG:4326, −10…10°):
/// the PNG tiles are decoded back with GDAL and their pixels checked.
/// </summary>
public sealed class CogRasterTileSourceTests : IDisposable
{
    private static readonly byte[] PngSignature = [0x89, (byte)'P', (byte)'N', (byte)'G'];

    private readonly TempDirectory _directory = new();

    public void Dispose() => _directory.Dispose();

    private static TileRequest Request(TileAddress tile, string layer = "ortho") => new(new TileLayerSet("set", [layer]), tile);

    [Fact]
    public async Task ATileInsideTheRaster_IsAnOpaque256PxPngOfItsColors()
    {
        var source = new CogRasterTileSource("ortho", TestRasters.WriteCog(_directory.Path, (200, 30, 10)));

        var payload = await source.GetRasterTileAsync(Request(TileAddress.Containing(1, 1, 6)), CancellationToken.None);

        payload.ContentType.Should().Be(TileFormats.PngContentType);
        payload.ContentEncoding.Should().BeNull();
        payload.Content.Span[..4].ToArray().Should().Equal(PngSignature);
        var bands = TestRasters.DecodePng(payload.Content, out var width, out var height);
        (width, height).Should().Be((256, 256));
        bands.Should().HaveCount(4, "RGB plus the alpha band");
        bands[0].Should().OnlyContain(v => v == 200);
        bands[1].Should().OnlyContain(v => v == 30);
        bands[2].Should().OnlyContain(v => v == 10);
        bands[3].Should().OnlyContain(v => v == 255);
    }

    [Fact]
    public async Task ATileAcrossTheRastersEdge_IsTransparentBeyondIt()
    {
        var source = new CogRasterTileSource("ortho", TestRasters.WriteCog(_directory.Path, (0, 255, 0)));
        var tile = TileAddress.Containing(1, 1, 4);

        var payload = await source.GetRasterTileAsync(Request(tile), CancellationToken.None);

        var bands = TestRasters.DecodePng(payload.Content, out var width, out _);
        var alpha = bands[3];
        alpha[0].Should().Be(0, "the tile's north-west corner ({0}) is north of the raster", tile.Envelope4326);
        alpha[(255 * width) + 0].Should().Be(255, "its south-west corner is inside the raster");
        bands[1][(255 * width) + 0].Should().Be(255);
    }

    [Fact]
    public async Task ATileTheRasterDoesNotReach_IsEmpty()
    {
        var source = new CogRasterTileSource("ortho", TestRasters.WriteCog(_directory.Path, (1, 2, 3)));

        var payload = await source.GetRasterTileAsync(Request(TileAddress.Containing(120, 60, 6)), CancellationToken.None);

        payload.IsEmpty.Should().BeTrue();
        payload.ContentType.Should().Be(TileFormats.PngContentType);
    }

    [Fact]
    public async Task ALayerSetWithoutTheSourcesLayer_IsEmpty_WithoutReadingTheRaster()
    {
        var source = new CogRasterTileSource("ortho", _directory.File("never-read.tif"));

        var payload = await source.GetRasterTileAsync(Request(new TileAddress(0, 0, 0), layer: "other"), CancellationToken.None);

        payload.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public async Task AFloatRaster_IsStretchedToEightBits()
    {
        var source = new CogRasterTileSource("dem", TestRasters.WriteCog(_directory.Path, default, float32: true));

        var payload = await source.GetRasterTileAsync(Request(TileAddress.Containing(0, 0, 3), "dem"), CancellationToken.None);

        var bands = TestRasters.DecodePng(payload.Content, out var width, out _);
        bands.Should().HaveCount(2, "one grey band plus alpha");
        var row = Enumerable.Range(0, width).Where(c => bands[1][(10 * width) + c] == 255).Select(c => bands[0][(10 * width) + c]).ToArray();
        row.Should().NotBeEmpty();
        row.Should().BeInAscendingOrder("the ramp rises eastwards");
        row[^1].Should().BeGreaterThan(row[0]);
    }

    [Fact]
    public Task AMissingRaster_Throws()
    {
        var source = new CogRasterTileSource("ortho", _directory.File("missing.tif"));

        var act = () => source.GetRasterTileAsync(Request(new TileAddress(0, 0, 0)), CancellationToken.None);

        return act.Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact]
    public async Task AFileGdalCannotRead_ThrowsInvalidData()
    {
        var path = _directory.File("garbage.tif");
        await File.WriteAllTextAsync(path, "not a raster");
        var source = new CogRasterTileSource("ortho", path);

        var act = () => source.GetRasterTileAsync(Request(new TileAddress(0, 0, 0)), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task ACancelledRequest_Throws()
    {
        var source = new CogRasterTileSource("ortho", TestRasters.WriteCog(_directory.Path, (1, 2, 3)));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => source.GetRasterTileAsync(Request(new TileAddress(0, 0, 0)), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ConcurrentTiles_EachOpenTheirOwnDataset()
    {
        var source = new CogRasterTileSource("ortho", TestRasters.WriteCog(_directory.Path, (9, 9, 9)));
        var tiles = Enumerable.Range(0, 4).SelectMany(x => Enumerable.Range(0, 4).Select(y => new TileAddress(5, 14 + x, 14 + y))).ToArray();

        var payloads = await Task.WhenAll(tiles.Select(t => Task.Run(() => source.GetRasterTileAsync(Request(t), CancellationToken.None))));

        payloads.Should().Contain(p => !p.IsEmpty);
    }

    [Theory]
    [InlineData("")]
    [InlineData("bad/id")]
    public void AnInvalidLayerId_IsRefused(string layerId)
    {
        var act = () => new CogRasterTileSource(layerId, "x.tif");

        act.Should().Throw<ArgumentException>();
    }

    // ── DescribeAsync (XD01-459) ──────────────────────────────────────────────

    [Fact]
    public async Task Describe_IsItsOneLayer_FromZoom0ToItsNativeZoom_OverItsWarpedExtent()
    {
        var source = new CogRasterTileSource("ortho", TestRasters.WriteCog(_directory.Path, (200, 30, 10)));

        var description = await source.DescribeAsync(CancellationToken.None);

        description.LayerIds.Should().Equal("ortho");
        description.MinZoom.Should().Be(0);
        description.MaxZoom.Should().Be(4, "0.1° pixels are about 11 km at the equator: zoom 4's 256 px tiles are about 9.8 km");
        var bounds = description.Bounds!.Value;
        bounds.MinLon.Should().BeApproximately(-10, 0.15);
        bounds.MaxLon.Should().BeApproximately(10, 0.15);
        bounds.MinLat.Should().BeApproximately(-10, 0.15);
        bounds.MaxLat.Should().BeApproximately(10, 0.15);
        description.Version.Should().MatchRegex("^[0-9a-f]{16}$");
        (await source.DescribeAsync(CancellationToken.None)).Should().BeSameAs(description, "nothing changed: read with GDAL once");
    }

    [Fact]
    public async Task Describe_AReplacedFile_IsANewVersion()
    {
        var path = TestRasters.WriteCog(_directory.Path, (1, 2, 3));
        var source = new CogRasterTileSource("ortho", path);
        var before = await source.DescribeAsync(CancellationToken.None);

        File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddMinutes(1));
        var after = await source.DescribeAsync(CancellationToken.None);

        after.Version.Should().NotBe(before.Version);
        after.MaxZoom.Should().Be(before.MaxZoom);
    }

    [Fact]
    public Task Describe_AMissingFile_FailsWithFileNotFound()
    {
        var source = new CogRasterTileSource("ortho", Path.Combine(_directory.Path, "gone.tif"));

        var describe = () => source.DescribeAsync(CancellationToken.None);

        return describe.Should().ThrowAsync<FileNotFoundException>();
    }

    [Theory]
    [InlineData(156_543.03392804097, 0)]
    [InlineData(156_543.0, 1)]
    [InlineData(11_132.0, 4)]
    [InlineData(0.15, 20)]
    [InlineData(0.000_001, TileAddress.MaxZoom)]
    [InlineData(10_000_000.0, 0)]
    [InlineData(0.0, TileAddress.MaxZoom)]
    [InlineData(double.NaN, TileAddress.MaxZoom)]
    public void NativeZoom_IsTheFirstZoomAsFineAsThePixels_WithinThePyramid(double pixelSize, int zoom) =>
        CogRasterTileSource.NativeZoom(pixelSize).Should().Be(zoom);
}
