using FluentAssertions;
using GeoAssets.Core.Rendering;
using GeoAssets.Provider.PMTiles.Packs;
using Xunit;

namespace GeoAssets.Provider.PMTiles.Tests;

/// <summary>
/// XD01-464 — managing the packs a <see cref="PMTilesPackStorage"/> keeps: listing them (vector and raster) with their
/// import record, size and header, the storage they use, and deleting one — also while it is open.
/// </summary>
public sealed class PMTilesPackStorageManagementTests : IDisposable
{
    private static readonly DateTimeOffset FirstImport = new(2026, 11, 20, 9, 30, 0, TimeSpan.Zero);

    private readonly TempDirectory _picked = new();
    private readonly TempDirectory _appData = new();
    private readonly FixedClock _clock = new(FirstImport);

    private string PacksDirectory => Path.Combine(_appData.Path, "offline-packs");

    public void Dispose()
    {
        _picked.Dispose();
        _appData.Dispose();
    }

    private PMTilesPackStorage Storage() => new(PacksDirectory, timeProvider: _clock);

    private string Picked(string path, string fileName)
    {
        var named = Path.Combine(_picked.Path, fileName);
        File.Move(path, named);
        return named;
    }

    private string VectorPack(string fileName = "north-grid.pmtiles") =>
        Picked(TestPacks.Write(_picked.Path, [new TileAddress(0, 0, 0), new TileAddress(3, 4, 2)], maxZoom: 12, minZoom: 2,
            bounds: (-38.6, -13.1, -38.3, -12.8)), fileName);

    private string RasterPack(string fileName = "basemap.pmtiles") =>
        Picked(TestRasterTiles.Archive(PMTilesTileType.Png, [new TileAddress(4, 7, 6)], minZoom: 4, maxZoom: 9,
            bounds: (-9.5, 38.6, -9.0, 38.9)).WriteTo(_picked.Path), fileName);

    [Fact]
    public async Task NothingImported_ListsNothing_AndUsesNoBytes()
    {
        var storage = Storage();

        (await storage.ListAsync(CancellationToken.None)).Should().BeEmpty();
        (await storage.GetUsedBytesAsync(CancellationToken.None)).Should().Be(0);
    }

    [Fact]
    public async Task ImportedVectorAndRasterPacks_AreListedNewestFirst_WithTheirRecordSizeAndHeader()
    {
        var storage = Storage();
        string vectorId, rasterId;
        using (var vector = await storage.ImportAsync(VectorPack(), CancellationToken.None))
            vectorId = vector.Id;
        _clock.Now = FirstImport.AddDays(2);
        using (var raster = await storage.ImportAsync(RasterPack(), CancellationToken.None))
            rasterId = raster.Id;

        var packs = await storage.ListAsync(CancellationToken.None);

        packs.Select(p => p.Id).Should().Equal(rasterId, vectorId);
        var (newest, oldest) = (packs[0], packs[1]);

        newest.Kind.Should().Be(PMTilesPackKind.Raster);
        newest.FileName.Should().Be("basemap.pmtiles");
        newest.ImportedAt.Should().Be(FirstImport.AddDays(2));
        newest.Path.Should().Be(Path.Combine(PacksDirectory, rasterId + ".pmtiles"));
        newest.SizeBytes.Should().Be(new FileInfo(newest.Path).Length);
        newest.Header!.MinZoom.Should().Be(4);
        newest.Header.MaxZoom.Should().Be(9);
        newest.Header.Bounds.Should().Be((-9.5, 38.6, -9.0, 38.9));

        oldest.Kind.Should().Be(PMTilesPackKind.Vector);
        oldest.FileName.Should().Be("north-grid.pmtiles");
        oldest.ImportedAt.Should().Be(FirstImport);
        oldest.Header!.MinZoom.Should().Be(2);
        oldest.Header.MaxZoom.Should().Be(12);
        oldest.Header.Bounds.Should().Be((-38.6, -13.1, -38.3, -12.8));

        (await storage.GetUsedBytesAsync(CancellationToken.None)).Should().Be(newest.SizeBytes + oldest.SizeBytes);
        Directory.GetFiles(PacksDirectory).Should().HaveCount(2, "the import records are kept apart from the packs");
    }

    [Fact]
    public async Task APackPickedAgain_KeepsItsFirstImportRecord()
    {
        var storage = Storage();
        var source = VectorPack();
        using (await storage.ImportAsync(source, CancellationToken.None))
        {
        }

        _clock.Now = FirstImport.AddDays(5);
        using (await storage.ImportAsync(source, CancellationToken.None))
        {
        }

        (await storage.ListAsync(CancellationToken.None)).Should().ContainSingle().Which.ImportedAt.Should().Be(FirstImport);
    }

    [Fact]
    public async Task APackWithoutAnImportRecord_ListsItsFilesLastWrite_AndNoFileName()
    {
        var storage = Storage();
        string path;
        using (var pack = await storage.ImportAsync(VectorPack(), CancellationToken.None))
            path = pack.Path;
        Directory.Delete(Path.Combine(PacksDirectory, PMTilesPackStorage.InfoDirectoryName), recursive: true);
        var written = new DateTime(2025, 6, 1, 8, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, written);

        var listed = (await storage.ListAsync(CancellationToken.None)).Should().ContainSingle().Which;

        listed.FileName.Should().BeNull();
        listed.ImportedAt.Should().Be(new DateTimeOffset(written));
    }

    [Fact]
    public async Task ADamagedRecord_IsIgnored()
    {
        var storage = Storage();
        string id;
        using (var pack = await storage.ImportAsync(VectorPack(), CancellationToken.None))
            id = pack.Id;
        await File.WriteAllTextAsync(Path.Combine(PacksDirectory, PMTilesPackStorage.InfoDirectoryName, id + ".json"), "{ not json");

        var listed = (await storage.ListAsync(CancellationToken.None)).Should().ContainSingle().Which;

        listed.FileName.Should().BeNull();
        listed.Kind.Should().Be(PMTilesPackKind.Vector);
    }

    [Fact]
    public async Task ADamagedPack_IsListedWithoutAHeader_SoItCanStillBeDeleted()
    {
        Directory.CreateDirectory(PacksDirectory);
        var damaged = Path.Combine(PacksDirectory, "0123456789abcdef.pmtiles");
        await File.WriteAllBytesAsync(damaged, new byte[40]);
        var storage = Storage();

        var listed = (await storage.ListAsync(CancellationToken.None)).Should().ContainSingle().Which;

        listed.Id.Should().Be("0123456789abcdef");
        listed.Header.Should().BeNull();
        listed.Kind.Should().BeNull();
        listed.SizeBytes.Should().Be(40);
        storage.Delete(listed.Id).Should().BeTrue();
        File.Exists(damaged).Should().BeFalse();
    }

    [Fact]
    public async Task AnImportUnderWay_AndFilesThatAreNotPacks_AreNotListed()
    {
        var storage = Storage();
        using (await storage.ImportAsync(VectorPack(), CancellationToken.None))
        {
        }

        await File.WriteAllBytesAsync(Path.Combine(PacksDirectory, "0123456789abcdef.pmtiles.1f2e.partial"), new byte[10]);
        await File.WriteAllBytesAsync(Path.Combine(PacksDirectory, "holiday.pmtiles"), new byte[10]);
        await File.WriteAllBytesAsync(Path.Combine(PacksDirectory, "notes.txt"), new byte[10]);

        (await storage.ListAsync(CancellationToken.None)).Should().ContainSingle();
    }

    [Fact]
    public async Task Deleting_RemovesThePackAndItsRecord_AndFreesItsBytes()
    {
        var storage = Storage();
        string deleted, kept;
        using (var vector = await storage.ImportAsync(VectorPack(), CancellationToken.None))
            deleted = vector.Id;
        using (var raster = await storage.ImportAsync(RasterPack(), CancellationToken.None))
            kept = raster.Id;
        var keptBytes = new FileInfo(storage.PathOf(kept)).Length;

        storage.Delete(deleted).Should().BeTrue();

        File.Exists(storage.PathOf(deleted)).Should().BeFalse();
        File.Exists(Path.Combine(PacksDirectory, PMTilesPackStorage.InfoDirectoryName, deleted + ".json")).Should().BeFalse();
        (await storage.ListAsync(CancellationToken.None)).Select(p => p.Id).Should().Equal(kept);
        (await storage.GetUsedBytesAsync(CancellationToken.None)).Should().Be(keptBytes);
        storage.Delete(deleted).Should().BeFalse("it is gone already");
    }

    [Fact]
    public void DeletingFromAStorageNotCreatedYet_IsANoOp()
    {
        Storage().Delete("0123456789abcdef").Should().BeFalse();
    }

    [Theory]
    [InlineData("../../secrets")]
    [InlineData("0123456789ABCDEF")]
    [InlineData("0123456789abcde")]
    [InlineData("")]
    public void AnythingButAPackId_IsRefused(string packId)
    {
        var storage = Storage();

        FluentActions.Invoking(() => storage.Delete(packId)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => storage.PathOf(packId)).Should().Throw<ArgumentException>();
        PMTilesPackStorage.IsPackId(packId).Should().BeFalse();
    }

    [Fact]
    public async Task AnOpenVectorPack_CanBeDeleted_AndKeepsReadingUntilDisposed()
    {
        var storage = Storage();
        var pack = await storage.ImportAsync(VectorPack(), CancellationToken.None);

        storage.Delete(pack.Id).Should().BeTrue();

        File.Exists(pack.Path).Should().BeFalse();
        (await pack.Archive.GetTileAsync(new TileAddress(3, 4, 2), CancellationToken.None)).Should().NotBeNull("the open handle still reads the deleted file");
        pack.Dispose();
        var afterDispose = () => pack.Archive.GetTileAsync(new TileAddress(3, 4, 2), CancellationToken.None);
        await afterDispose.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task AnOpenRasterPack_CanBeDeleted_AndKeepsServingItsImages()
    {
        var storage = Storage();
        using var provider = new PMTilesRasterPackAssetProvider(await storage.ImportAsync(RasterPack(), CancellationToken.None));

        storage.Delete(provider.Pack.Id).Should().BeTrue();

        var payload = await provider.RasterTiles.GetRasterTileAsync(
            new TileRequest(provider.RasterTileLayer.LayerSet, new TileAddress(4, 7, 6)), CancellationToken.None);
        payload.Content.ToArray().Should().Equal(TestRasterTiles.Png);
        (await storage.ListAsync(CancellationToken.None)).Should().BeEmpty();
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
