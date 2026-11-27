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
    public Task ARasterPack_IsRefused()
    {
        var path = new PMTilesTestArchive { TileType = PMTilesTileType.Png }.Add(new TileAddress(0, 0, 0), [1, 2, 3]).WriteTo(_directory.Path);

        var act = () => PMTilesPack.OpenAsync(path, CancellationToken.None);

        return act.Should().ThrowAsync<PMTilesFormatException>().WithMessage("*Png tiles*vector tiles*");
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
