using System.IO.Compression;
using FluentAssertions;
using GeoAssets.Core.Rendering;
using GeoAssets.Provider.PMTiles.Packs;
using Xunit;

namespace GeoAssets.Provider.PMTiles.Tests;

/// <summary>
/// XD01-438 — <see cref="PMTilesPackTiles"/>: an offline pack's tiles are read through the host's tile cache — cache
/// first, the pack only on a miss, then cached as stored (an absent tile as empty) — and answered decompressed.
/// </summary>
public sealed class PMTilesPackTilesTests : IDisposable
{
    private static readonly TileAddress Tile = new(2, 1, 1);

    private readonly TempDirectory _directory = new();

    public void Dispose() => _directory.Dispose();

    private Task<PMTilesPack> PackAsync() =>
        PMTilesPack.OpenAsync(TestPacks.Write(_directory.Path, [Tile]), CancellationToken.None);

    [Fact]
    public async Task AMiss_IsReadFromThePack_AndCachedAsStored()
    {
        using var pack = await PackAsync();
        var cache = new RecordingTileCache();
        var tiles = new PMTilesPackTiles(pack, cache);

        var tile = await tiles.GetTileAsync(Tile, CancellationToken.None);

        tile.Should().Equal(PMTilesTestArchive.Gunzip(TestPacks.PointTile(Tile.ToString())));
        var key = PMTilesPackTiles.KeyOf(pack.Id, Tile).Value;
        cache.Sets.Should().Equal(key);
        cache.Entries[key].ContentEncoding.Should().Be("gzip");
        cache.Entries[key].ContentType.Should().Be(TileFormats.MapboxVectorTileContentType);
        cache.Entries[key].Content.ToArray().Should().Equal(TestPacks.PointTile(Tile.ToString()), "the tile is cached compressed, as the pack stores it");
    }

    [Fact]
    public async Task AHit_IsServedFromTheCache_WithoutWritingAgain()
    {
        using var pack = await PackAsync();
        var cache = new RecordingTileCache();
        var cached = PMTilesTestArchive.Gzip(TestMvt.Encode(new TestMvt.Layer("cached", [TestMvt.PointAt(1, 1)])));
        cache.Entries[PMTilesPackTiles.KeyOf(pack.Id, Tile).Value] = new TilePayload(cached, TileFormats.MapboxVectorTileContentType, "gzip");
        var tiles = new PMTilesPackTiles(pack, cache);

        var tile = await tiles.GetTileAsync(Tile, CancellationToken.None);

        tile.Should().Equal(PMTilesTestArchive.Gunzip(cached), "a cached tile is not read from the pack again");
        cache.Sets.Should().BeEmpty();
    }

    [Fact]
    public async Task ATileThePackDoesNotHave_IsNull_AndCachedEmpty()
    {
        using var pack = await PackAsync();
        var cache = new RecordingTileCache();
        var tiles = new PMTilesPackTiles(pack, cache);
        var absent = new TileAddress(2, 3, 3);

        (await tiles.GetTileAsync(absent, CancellationToken.None)).Should().BeNull();
        (await tiles.GetTileAsync(absent, CancellationToken.None)).Should().BeNull();

        cache.Sets.Should().ContainSingle();
        cache.Entries.Single().Value.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public async Task WithoutACache_TilesAreReadFromThePack()
    {
        using var pack = await PackAsync();
        var tiles = new PMTilesPackTiles(pack, cache: null);

        (await tiles.GetTileAsync(Tile, CancellationToken.None)).Should().NotBeNull();
    }

    [Fact]
    public async Task TheCacheKey_IsScopedToTheOfflinePack_AndVersionedByThePacksId()
    {
        using var pack = await PackAsync();

        var key = PMTilesPackTiles.KeyOf(pack.Id, Tile);

        key.Value.Should().Be($"tile/offline-pack/pmtiles/pmtiles/{pack.Id}/2/1/1.mvt");
    }

    [Fact]
    public async Task ABrotliCachedTile_IsDecompressed()
    {
        using var pack = await PackAsync();
        var cache = new RecordingTileCache();
        var raw = TestMvt.Encode(new TestMvt.Layer("br", [TestMvt.PointAt(1, 1)]));
        using var output = new MemoryStream();
        await using (var brotli = new BrotliStream(output, CompressionLevel.Fastest, leaveOpen: true))
            await brotli.WriteAsync(raw);
        cache.Entries[PMTilesPackTiles.KeyOf(pack.Id, Tile).Value] = new TilePayload(output.ToArray(), TileFormats.MapboxVectorTileContentType, "br");

        (await new PMTilesPackTiles(pack, cache).GetTileAsync(Tile, CancellationToken.None)).Should().Equal(raw);
    }

    [Fact]
    public async Task ACachedTileOfAnUnknownEncoding_IsAFormatError()
    {
        using var pack = await PackAsync();
        var cache = new RecordingTileCache();
        cache.Entries[PMTilesPackTiles.KeyOf(pack.Id, Tile).Value] = new TilePayload(new byte[] { 1 }, TileFormats.MapboxVectorTileContentType, "zstd");

        var act = () => new PMTilesPackTiles(pack, cache).GetTileAsync(Tile, CancellationToken.None);

        await act.Should().ThrowAsync<PMTilesFormatException>();
    }
}
