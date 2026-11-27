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

    [Theory]
    [InlineData(PMTilesTileType.Png)]
    [InlineData(PMTilesTileType.Webp)]
    public async Task ARasterPack_IsValidatedAndStreamedInLikeAVectorPack(PMTilesTileType tileType)
    {
        var source = TestRasterTiles.Archive(tileType, [new TileAddress(0, 0, 0)]).WriteTo(_picked.Path);
        var storage = new PMTilesPackStorage(PacksDirectory);

        using var pack = await storage.ImportAsync(source, CancellationToken.None);

        pack.Kind.Should().Be(PMTilesPackKind.Raster);
        pack.TileType.Should().Be(tileType);
        pack.Path.Should().Be(Path.Combine(PacksDirectory, pack.Id + ".pmtiles"));
        (await File.ReadAllBytesAsync(pack.Path)).Should().Equal(await File.ReadAllBytesAsync(source));
        Directory.GetFiles(PacksDirectory).Should().ContainSingle("no temporary file is left behind");
    }

    [Fact]
    public async Task ACorruptRasterPack_IsRefused_BeforeAnythingIsCopied()
    {
        var bytes = TestRasterTiles.Archive(PMTilesTileType.Png, [new TileAddress(0, 0, 0)]).Build();
        var source = Path.Combine(_picked.Path, "basemap.pmtiles");
        await File.WriteAllBytesAsync(source, bytes.AsMemory(0, PMTilesHeader.Size + 2));
        var storage = new PMTilesPackStorage(PacksDirectory);

        var act = () => storage.ImportAsync(source, CancellationToken.None);

        await act.Should().ThrowAsync<PMTilesFormatException>();
        Directory.Exists(PacksDirectory).Should().BeFalse();
    }

    [Fact]
    public async Task APackLargerThanTheThreshold_IsNotCopied_UntilItsIdIsConfirmed()
    {
        var source = TestRasterTiles.Archive(PMTilesTileType.Png, [new TileAddress(0, 0, 0)]).WriteTo(_picked.Path);
        var length = new FileInfo(source).Length;
        var storage = new PMTilesPackStorage(PacksDirectory, largePackBytes: length - 1);

        var act = () => storage.ImportAsync(source, CancellationToken.None);

        var asked = (await act.Should().ThrowAsync<PMTilesPackSizeConfirmationException>()).Which;
        asked.PackId.Should().Be(await PMTilesPack.ComputeIdAsync(source, CancellationToken.None));
        asked.PackBytes.Should().Be(length);
        asked.LargePackBytes.Should().Be(length - 1);
        asked.FileName.Should().Be(Path.GetFileName(source));
        asked.Message.Should().Contain("is a large offline map pack (1 MB)").And.Contain("Press Connect again to import it");
        Directory.Exists(PacksDirectory).Should().BeFalse("nothing is copied before the user confirms");

        var wrong = () => storage.ImportAsync(source, "0000000000000000", CancellationToken.None);
        await wrong.Should().ThrowAsync<PMTilesPackSizeConfirmationException>("another pack's confirmation doesn't import this one");

        using var pack = await storage.ImportAsync(source, asked.PackId, CancellationToken.None);
        File.Exists(pack.Path).Should().BeTrue();
    }

    [Fact]
    public async Task APackAtTheThreshold_IsCopiedWithoutAsking()
    {
        var source = TestPacks.Write(_picked.Path, [new TileAddress(0, 0, 0)]);
        var storage = new PMTilesPackStorage(PacksDirectory, largePackBytes: new FileInfo(source).Length);

        using var pack = await storage.ImportAsync(source, CancellationToken.None);

        pack.Kind.Should().Be(PMTilesPackKind.Vector);
        File.Exists(pack.Path).Should().BeTrue();
    }

    [Fact]
    public async Task ALargePackAlreadyStored_IsNotAskedAboutAgain()
    {
        var source = TestPacks.Write(_picked.Path, [new TileAddress(0, 0, 0)]);
        string id;
        using (var first = await new PMTilesPackStorage(PacksDirectory).ImportAsync(source, CancellationToken.None))
            id = first.Id;
        var strict = new PMTilesPackStorage(PacksDirectory, largePackBytes: 1);

        using var again = await strict.ImportAsync(source, CancellationToken.None);
        using var reopened = await strict.ImportAsync(again.Path, CancellationToken.None);

        again.Id.Should().Be(id);
        reopened.Path.Should().Be(again.Path);
    }

    [Fact]
    public void TheThreshold_DefaultsTo1GiB_AndMustBePositive()
    {
        new PMTilesPackStorage(PacksDirectory).LargePackBytes.Should().Be(1024L * 1024 * 1024);
        FluentActions.Invoking(() => new PMTilesPackStorage(PacksDirectory, 0)).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(1, "1 MB")]
    [InlineData(5L * 1024 * 1024, "5 MB")]
    [InlineData(820L * 1024 * 1024, "820 MB")]
    [InlineData(1024L * 1024 * 1024, "1 GB")]
    [InlineData(3_650_722_201L, "3.4 GB")]
    public void ASize_IsWrittenForAPerson(long bytes, string expected)
    {
        PMTilesPackSizeConfirmationException.FormatSize(bytes).Should().Be(expected);
    }
}
