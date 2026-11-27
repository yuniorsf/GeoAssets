using FluentAssertions;
using GeoAssets.Core.Rendering;
using GeoAssets.Provider.PMTiles.Packs;
using Xunit;

namespace GeoAssets.Provider.PMTiles.Tests;

/// <summary>XD01-438 — <see cref="PMTilesPack"/>: what an offline map pack is, and its content fingerprint.</summary>
public sealed class PMTilesPackTests : IDisposable
{
    private readonly TempDirectory _directory = new();

    public void Dispose() => _directory.Dispose();

    [Fact]
    public async Task AVectorPack_Opens_WithAContentFingerprintAsItsId()
    {
        var path = TestPacks.Write(_directory.Path, [new TileAddress(0, 0, 0)]);

        using var pack = await PMTilesPack.OpenAsync(path, CancellationToken.None);

        pack.Id.Should().MatchRegex("^[0-9a-f]{16}$");
        pack.Path.Should().Be(Path.GetFullPath(path));
        pack.Header.TileType.Should().Be(PMTilesTileType.Mvt);
    }

    [Fact]
    public async Task TheSameContent_HasTheSameId_WhereverItIs_AndOtherContentAnother()
    {
        var path = TestPacks.Write(_directory.Path, [new TileAddress(0, 0, 0)]);
        var copy = _directory.File("copy.pmtiles");
        File.Copy(path, copy);
        var other = TestPacks.Write(_directory.Path, [new TileAddress(1, 0, 0)]);

        var id = await PMTilesPack.ComputeIdAsync(path, CancellationToken.None);

        (await PMTilesPack.ComputeIdAsync(copy, CancellationToken.None)).Should().Be(id);
        (await PMTilesPack.ComputeIdAsync(other, CancellationToken.None)).Should().NotBe(id);
    }

    [Fact]
    public async Task AVectorPack_IsAVectorPack()
    {
        using var pack = await PMTilesPack.OpenAsync(TestPacks.Write(_directory.Path, [new TileAddress(0, 0, 0)]), CancellationToken.None);

        pack.Kind.Should().Be(PMTilesPackKind.Vector);
        pack.TileType.Should().Be(PMTilesTileType.Mvt);
    }

    [Fact]
    public async Task APackOfAnUndeclaredTileType_IsStillReadAsVector()
    {
        var path = new PMTilesTestArchive { TileType = PMTilesTileType.Unknown }.Add(new TileAddress(0, 0, 0), TestPacks.PointTile("a")).WriteTo(_directory.Path);

        using var pack = await PMTilesPack.OpenAsync(path, CancellationToken.None);

        pack.Kind.Should().Be(PMTilesPackKind.Vector);
    }

    [Theory]
    [InlineData(PMTilesTileType.Png)]
    [InlineData(PMTilesTileType.Webp)]
    [InlineData(PMTilesTileType.Jpeg)]
    public async Task ARasterPack_Opens_AsARasterPack(PMTilesTileType tileType)
    {
        var path = TestRasterTiles.Archive(tileType, [new TileAddress(0, 0, 0)]).WriteTo(_directory.Path);

        using var pack = await PMTilesPack.OpenAsync(path, CancellationToken.None);

        pack.Kind.Should().Be(PMTilesPackKind.Raster);
        pack.TileType.Should().Be(tileType);
        pack.Id.Should().MatchRegex("^[0-9a-f]{16}$");
    }

    [Fact]
    public Task AnAvifPack_IsRefused()
    {
        var path = new PMTilesTestArchive { TileType = PMTilesTileType.Avif }.Add(new TileAddress(0, 0, 0), [1, 2, 3]).WriteTo(_directory.Path);

        var act = () => PMTilesPack.OpenAsync(path, CancellationToken.None);

        return act.Should().ThrowAsync<PMTilesFormatException>().WithMessage("*Avif tiles*vector tiles (MVT) or PNG, JPEG or WebP images*");
    }

    [Theory]
    [InlineData(9, 4)]
    [InlineData(25, 26)]
    public Task ARasterPackWithoutZoomsAMapCanShow_IsRefused(byte minZoom, byte maxZoom)
    {
        var path = TestRasterTiles.Archive(PMTilesTileType.Png, [], minZoom, maxZoom).WriteTo(_directory.Path);

        var act = () => PMTilesPack.OpenAsync(path, CancellationToken.None);

        return act.Should().ThrowAsync<PMTilesFormatException>().WithMessage("*zooms*not ones a map can show*");
    }

    [Fact]
    public Task AZstandardPack_IsRefused()
    {
        var path = new PMTilesTestArchive { TileCompression = PMTilesCompression.Zstd }.Add(new TileAddress(0, 0, 0), [1, 2, 3]).WriteTo(_directory.Path);

        var act = () => PMTilesPack.OpenAsync(path, CancellationToken.None);

        return act.Should().ThrowAsync<PMTilesFormatException>().WithMessage("*Zstd-compressed*");
    }

    [Fact]
    public async Task AFileThatIsNotPMTiles_IsRefused()
    {
        var path = _directory.File("notes.pmtiles");
        await File.WriteAllBytesAsync(path, new byte[200]);

        var act = () => PMTilesPack.OpenAsync(path, CancellationToken.None);

        await act.Should().ThrowAsync<PMTilesFormatException>();
    }

    [Fact]
    public Task AMissingFile_IsNotFound()
    {
        var act = () => PMTilesPack.OpenAsync(_directory.File("gone.pmtiles"), CancellationToken.None);

        return act.Should().ThrowAsync<FileNotFoundException>();
    }
}
