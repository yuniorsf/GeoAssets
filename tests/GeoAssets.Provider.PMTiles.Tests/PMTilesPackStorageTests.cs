using FluentAssertions;
using GeoAssets.Core.Rendering;
using GeoAssets.Provider.PMTiles.Packs;
using Xunit;

namespace GeoAssets.Provider.PMTiles.Tests;

/// <summary>
/// XD01-438 — <see cref="PMTilesPackStorage.ImportAsync"/>: a picked pack is validated, then streamed into the storage's
/// folder as <c>&lt;id&gt;.pmtiles</c> (never twice, never half), and a stored pack reopens where it is.
/// </summary>
public sealed class PMTilesPackStorageTests : IDisposable
{
    private readonly TempDirectory _picked = new();
    private readonly TempDirectory _appData = new();

    private string PacksDirectory => Path.Combine(_appData.Path, "offline-packs");

    public void Dispose()
    {
        _picked.Dispose();
        _appData.Dispose();
    }

    [Fact]
    public async Task APickedPack_IsCopiedIntoTheFolderUnderItsId_AndTheCopyOpened()
    {
        var source = TestPacks.Write(_picked.Path, [new TileAddress(0, 0, 0), new TileAddress(1, 1, 0)]);
        var storage = new PMTilesPackStorage(PacksDirectory);

        using var pack = await storage.ImportAsync(source, CancellationToken.None);

        pack.Path.Should().Be(Path.Combine(PacksDirectory, pack.Id + ".pmtiles"));
        (await File.ReadAllBytesAsync(pack.Path)).Should().Equal(await File.ReadAllBytesAsync(source));
        File.Exists(source).Should().BeTrue("the picked file is left where it was");
        Directory.GetFiles(PacksDirectory).Should().ContainSingle("no temporary file is left behind");
        (await pack.Archive.GetTileAsync(new TileAddress(1, 1, 0), CancellationToken.None)).Should().NotBeNull();
    }

    [Fact]
    public async Task ThePackPickedAgain_IsNotCopiedAgain()
    {
        var source = TestPacks.Write(_picked.Path, [new TileAddress(0, 0, 0)]);
        var storage = new PMTilesPackStorage(PacksDirectory);
        string stored;
        using (var first = await storage.ImportAsync(source, CancellationToken.None))
            stored = first.Path;
        var written = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(stored, written);

        using var second = await storage.ImportAsync(source, CancellationToken.None);

        second.Path.Should().Be(stored);
        File.GetLastWriteTimeUtc(stored).Should().Be(written);
    }

    [Fact]
    public async Task AStoredPack_ReopensWhereItIs()
    {
        var storage = new PMTilesPackStorage(PacksDirectory);
        string stored;
        using (var imported = await storage.ImportAsync(TestPacks.Write(_picked.Path, [new TileAddress(0, 0, 0)]), CancellationToken.None))
            stored = imported.Path;

        using var reopened = await storage.ImportAsync(stored, CancellationToken.None);

        reopened.Path.Should().Be(stored);
        Directory.GetFiles(PacksDirectory).Should().ContainSingle();
    }

    [Fact]
    public async Task AFileThatIsNotAPack_IsRefused_BeforeAnythingIsCopied()
    {
        var source = Path.Combine(_picked.Path, "photo.pmtiles");
        await File.WriteAllBytesAsync(source, new byte[4096]);
        var storage = new PMTilesPackStorage(PacksDirectory);

        var act = () => storage.ImportAsync(source, CancellationToken.None);

        await act.Should().ThrowAsync<PMTilesFormatException>();
        Directory.Exists(PacksDirectory).Should().BeFalse();
    }

    [Fact]
    public async Task ACancelledImport_LeavesNothingBehind()
    {
        var source = TestPacks.Write(_picked.Path, [new TileAddress(0, 0, 0)]);
        var storage = new PMTilesPackStorage(PacksDirectory);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var act = () => storage.ImportAsync(source, cancelled.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        (Directory.Exists(PacksDirectory) ? Directory.GetFiles(PacksDirectory) : []).Should().BeEmpty();
    }

    [Fact]
    public void ABlankDirectory_IsRefused()
    {
        var act = () => new PMTilesPackStorage(" ");

        act.Should().Throw<ArgumentException>();
    }
}
