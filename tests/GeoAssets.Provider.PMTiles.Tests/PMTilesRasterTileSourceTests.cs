using FluentAssertions;
using GeoAssets.Core.Rendering;
using Xunit;

namespace GeoAssets.Provider.PMTiles.Tests;

/// <summary>
/// XD01-465 — <see cref="PMTilesRasterTileSource"/>: the header of a PNG/WebP/JPEG archive, its tiles answered as the
/// archive stores the images in the archive's media type, an absent tile empty, and a corrupt archive or tile — or an
/// archive that isn't raster — refused with <see cref="PMTilesFormatException"/>.
/// </summary>
public sealed class PMTilesRasterTileSourceTests : IDisposable
{
    private static readonly TileLayerSet Layers = new("pmtiles", ["pmtiles"]);
    private static readonly TileAddress Tile = new(3, 4, 2);

    private readonly TempDirectory _directory = new();

    public void Dispose() => _directory.Dispose();

    private static TileRequest At(TileAddress tile) => new(Layers, tile);

    [Theory]
    [InlineData(PMTilesTileType.Png)]
    [InlineData(PMTilesTileType.Webp)]
    [InlineData(PMTilesTileType.Jpeg)]
    public async Task ARasterArchivesHeader_IsRead(PMTilesTileType tileType)
    {
        var path = TestRasterTiles.Archive(tileType, [Tile], minZoom: 2, maxZoom: 9, bounds: (-9.5, 38.6, -9.0, 38.9)).WriteTo(_directory.Path);

        using var archive = await PMTilesArchive.OpenAsync(path, CancellationToken.None);

        archive.Header.TileType.Should().Be(tileType);
        archive.Header.TileCompression.Should().Be(PMTilesCompression.None);
        archive.Header.MinZoom.Should().Be(2);
        archive.Header.MaxZoom.Should().Be(9);
        archive.Header.Bounds.West.Should().BeApproximately(-9.5, 1e-7);
        archive.Header.Bounds.North.Should().BeApproximately(38.9, 1e-7);
        archive.VectorLayerIds.Should().BeEmpty();
    }

    [Theory]
    [InlineData(PMTilesTileType.Png, "image/png")]
    [InlineData(PMTilesTileType.Webp, "image/webp")]
    [InlineData(PMTilesTileType.Jpeg, "image/jpeg")]
    public async Task ATile_IsTheStoredImage_InTheArchivesMediaType(PMTilesTileType tileType, string contentType)
    {
        using var source = new PMTilesRasterTileSource(TestRasterTiles.Archive(tileType, [Tile]).WriteTo(_directory.Path));

        var payload = await source.GetRasterTileAsync(At(Tile), CancellationToken.None);

        payload.ContentType.Should().Be(contentType);
        payload.ContentEncoding.Should().BeNull("an image is served as is, never content-encoded");
        payload.Content.ToArray().Should().Equal(TestRasterTiles.ImageOf(tileType));
    }

    [Fact]
    public async Task ATileTheArchiveDoesNotHave_IsEmpty()
    {
        using var source = new PMTilesRasterTileSource(TestRasterTiles.Archive(PMTilesTileType.Png, [Tile]).WriteTo(_directory.Path));

        var payload = await source.GetRasterTileAsync(At(new TileAddress(3, 0, 0)), CancellationToken.None);

        payload.IsEmpty.Should().BeTrue();
        payload.ContentType.Should().Be("image/png");
    }

    [Fact]
    public async Task AGzipStoredImage_IsAnsweredDecompressed()
    {
        var path = new PMTilesTestArchive { TileType = PMTilesTileType.Png, TileCompression = PMTilesCompression.Gzip }
            .Add(Tile, PMTilesTestArchive.Gzip(TestRasterTiles.Png))
            .WriteTo(_directory.Path);
        using var source = new PMTilesRasterTileSource(path);

        var payload = await source.GetRasterTileAsync(At(Tile), CancellationToken.None);

        payload.Content.ToArray().Should().Equal(TestRasterTiles.Png);
    }

    [Fact]
    public async Task OverAnOpenArchive_TheSourceLeavesItOpen()
    {
        using var archive = await PMTilesArchive.OpenAsync(TestRasterTiles.Archive(PMTilesTileType.Webp, [Tile]).WriteTo(_directory.Path), CancellationToken.None);
        var source = new PMTilesRasterTileSource(archive);

        (await source.GetRasterTileAsync(At(Tile), CancellationToken.None)).ContentType.Should().Be("image/webp");
        source.Dispose();

        (await archive.GetTileAsync(Tile, CancellationToken.None)).Should().NotBeNull("disposing the source doesn't close an archive it was given");
        source.Path.Should().Be(archive.Path);
    }

    [Fact]
    public async Task AVectorArchive_IsRefused()
    {
        using var source = new PMTilesRasterTileSource(new PMTilesTestArchive().Add(Tile, [1, 2, 3]).WriteTo(_directory.Path));

        var act = () => source.GetRasterTileAsync(At(Tile), CancellationToken.None);

        await act.Should().ThrowAsync<PMTilesFormatException>().WithMessage("*Mvt tiles, not PNG, JPEG or WebP images*");
    }

    [Fact]
    public async Task AnOpenVectorArchive_IsRefused()
    {
        using var archive = await PMTilesArchive.OpenAsync(new PMTilesTestArchive().Add(Tile, [1, 2, 3]).WriteTo(_directory.Path), CancellationToken.None);

        var act = () => new PMTilesRasterTileSource(archive);

        act.Should().Throw<PMTilesFormatException>();
    }

    [Fact]
    public async Task AZstandardRasterArchive_IsRefused()
    {
        var path = new PMTilesTestArchive { TileType = PMTilesTileType.Png, TileCompression = PMTilesCompression.Zstd }.Add(Tile, [1, 2, 3]).WriteTo(_directory.Path);
        using var source = new PMTilesRasterTileSource(path);

        var act = () => source.GetRasterTileAsync(At(Tile), CancellationToken.None);

        await act.Should().ThrowAsync<PMTilesFormatException>().WithMessage("*Zstd-compressed*");
    }

    [Fact]
    public async Task ATruncatedArchive_IsRefusedAsCorrupt()
    {
        var bytes = TestRasterTiles.Archive(PMTilesTileType.Png, [Tile, new TileAddress(3, 5, 2)]).Build();
        var path = _directory.File("truncated.pmtiles");
        await File.WriteAllBytesAsync(path, bytes.AsMemory(0, bytes.Length - TestRasterTiles.Png.Length - 4));
        using var source = new PMTilesRasterTileSource(path);

        var act = () => source.GetRasterTileAsync(At(new TileAddress(3, 5, 2)), CancellationToken.None);

        await act.Should().ThrowAsync<PMTilesFormatException>().WithMessage("*ends before byte*");
    }

    [Fact]
    public async Task ACorruptHeader_IsRefused()
    {
        var bytes = TestRasterTiles.Archive(PMTilesTileType.Png, [Tile]).Build();
        bytes[7] = 2;
        var path = _directory.File("v2.pmtiles");
        await File.WriteAllBytesAsync(path, bytes);
        using var source = new PMTilesRasterTileSource(path);

        var act = () => source.GetRasterTileAsync(At(Tile), CancellationToken.None);

        await act.Should().ThrowAsync<PMTilesFormatException>().WithMessage("*version 2 is not supported*");
    }

    [Theory]
    [InlineData(PMTilesTileType.Png)]
    [InlineData(PMTilesTileType.Webp)]
    [InlineData(PMTilesTileType.Jpeg)]
    public async Task ATileThatIsNotTheDeclaredImage_IsRefusedAsCorrupt(PMTilesTileType tileType)
    {
        var path = new PMTilesTestArchive { TileType = tileType, TileCompression = PMTilesCompression.None }
            .Add(Tile, [0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C])
            .WriteTo(_directory.Path);
        using var source = new PMTilesRasterTileSource(path);

        var act = () => source.GetRasterTileAsync(At(Tile), CancellationToken.None);

        await act.Should().ThrowAsync<PMTilesFormatException>().WithMessage($"*not a {tileType} image*");
    }

    [Fact]
    public async Task ACancelledRead_Throws()
    {
        using var source = new PMTilesRasterTileSource(TestRasterTiles.Archive(PMTilesTileType.Png, [Tile]).WriteTo(_directory.Path));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var act = () => source.GetRasterTileAsync(At(Tile), cancelled.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Arguments_AreChecked()
    {
        FluentActions.Invoking(() => new PMTilesRasterTileSource(" ")).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => new PMTilesRasterTileSource((PMTilesArchive)null!)).Should().Throw<ArgumentNullException>();
        using var source = new PMTilesRasterTileSource(TestRasterTiles.Archive(PMTilesTileType.Png, [Tile]).WriteTo(_directory.Path));
        await source.Invoking(s => s.GetRasterTileAsync(null!, CancellationToken.None)).Should().ThrowAsync<ArgumentNullException>();
    }

    [Theory]
    [InlineData(PMTilesTileType.Png, true)]
    [InlineData(PMTilesTileType.Jpeg, true)]
    [InlineData(PMTilesTileType.Webp, true)]
    [InlineData(PMTilesTileType.Avif, false)]
    [InlineData(PMTilesTileType.Mvt, false)]
    [InlineData(PMTilesTileType.Unknown, false)]
    public void TheSupportedRasterTypes_ArePngJpegAndWebp(PMTilesTileType tileType, bool supported)
    {
        PMTilesRasterImages.IsSupported(tileType).Should().Be(supported);
        if (!supported)
            FluentActions.Invoking(() => PMTilesRasterImages.ContentType(tileType)).Should().Throw<ArgumentOutOfRangeException>();
        PMTilesRasterImages.HasSignature(PMTilesTileType.Avif, TestRasterTiles.Png).Should().BeFalse();
    }
}
