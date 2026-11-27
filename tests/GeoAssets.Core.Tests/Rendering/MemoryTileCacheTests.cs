using FluentAssertions;
using GeoAssets.Core.Rendering;
using Xunit;

namespace GeoAssets.Core.Tests.Rendering;

/// <summary>
/// XD01-434 — <see cref="MemoryTileCache"/>: keyed by the full <see cref="TileCacheKey"/>, bounded by size (least recently
/// used first), and evicting every scope, layer set, version and format of the tiles of a range — whichever of the range
/// or the cached tiles of its zoom is smaller is walked.
/// </summary>
public sealed class MemoryTileCacheTests
{
    private static readonly TileCacheScope OrgA = TileCacheScope.Create("org-a", []);
    private static readonly TileCacheScope OrgB = TileCacheScope.Create("org-b", ["grant"]);
    private static readonly TileAddress Tile = new(12, 1503, 2230);

    private static TileCacheKey Key(TileAddress? address = null, TileCacheScope? scope = null, string version = "v1", TileFormat format = TileFormat.Mvt) =>
        new(scope ?? OrgA, "assets", version, address ?? Tile, format);

    private static TilePayload Payload(int bytes = 10) => new(new byte[bytes], TileFormats.MapboxVectorTileContentType);

    [Fact]
    public async Task Get_AnswersWhatWasSet_UnderThatExactKey_Only()
    {
        var cache = new MemoryTileCache();
        var payload = Payload();

        await cache.SetAsync(Key(), payload, CancellationToken.None);

        (await cache.GetAsync(Key(), CancellationToken.None)).Should().BeSameAs(payload);
        (await cache.GetAsync(Key(scope: OrgB), CancellationToken.None)).Should().BeNull("another scope never shares an entry");
        (await cache.GetAsync(Key(version: "v2"), CancellationToken.None)).Should().BeNull();
        (await cache.GetAsync(Key(format: TileFormat.Png), CancellationToken.None)).Should().BeNull();
        (await cache.GetAsync(Key(new TileAddress(12, 1503, 2231)), CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task Set_ReplacesAnEntry_AndCountsItsBytesOnce()
    {
        var cache = new MemoryTileCache();
        var second = Payload(30);

        await cache.SetAsync(Key(), Payload(10), CancellationToken.None);
        await cache.SetAsync(Key(), second, CancellationToken.None);

        (await cache.GetAsync(Key(), CancellationToken.None)).Should().BeSameAs(second);
        cache.Count.Should().Be(1);
        cache.Bytes.Should().Be(30 + MemoryTileCache.EntryOverheadBytes);
    }

    [Fact]
    public async Task OverItsBound_TheLeastRecentlyUsedEntriesGo()
    {
        var entry = 100 + MemoryTileCache.EntryOverheadBytes;
        var cache = new MemoryTileCache(maxBytes: 3 * entry);
        var tiles = Enumerable.Range(0, 4).Select(i => new TileAddress(10, i, 0)).ToArray();

        await cache.SetAsync(Key(tiles[0]), Payload(100), CancellationToken.None);
        await cache.SetAsync(Key(tiles[1]), Payload(100), CancellationToken.None);
        await cache.SetAsync(Key(tiles[2]), Payload(100), CancellationToken.None);
        await cache.GetAsync(Key(tiles[0]), CancellationToken.None); // tiles[1] is now the least recently used
        await cache.SetAsync(Key(tiles[3]), Payload(100), CancellationToken.None);

        (await cache.GetAsync(Key(tiles[1]), CancellationToken.None)).Should().BeNull();
        (await cache.GetAsync(Key(tiles[0]), CancellationToken.None)).Should().NotBeNull();
        (await cache.GetAsync(Key(tiles[2]), CancellationToken.None)).Should().NotBeNull();
        (await cache.GetAsync(Key(tiles[3]), CancellationToken.None)).Should().NotBeNull();
        cache.Bytes.Should().BeLessThanOrEqualTo(cache.MaxBytes);
    }

    [Fact]
    public async Task AnEntryLargerThanTheBound_IsNotKept()
    {
        var cache = new MemoryTileCache(maxBytes: 1000);

        await cache.SetAsync(Key(), Payload(2000), CancellationToken.None);

        (await cache.GetAsync(Key(), CancellationToken.None)).Should().BeNull();
        cache.Count.Should().Be(0);
        cache.Bytes.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Remove_EvictsEveryScopeVersionAndFormat_OfTheRangesTiles_AndNothingElse(bool manyCachedTiles)
    {
        var cache = new MemoryTileCache();
        var inside = new TileAddress(12, 1503, 2230);
        var outside = new TileAddress(12, 1510, 2230);
        var otherZoom = new TileAddress(13, 3006, 4460);
        foreach (var key in new[]
                 {
                     Key(inside), Key(inside, OrgB), Key(inside, version: "v2"), Key(inside, format: TileFormat.Png),
                     Key(outside), Key(otherZoom),
                 })
        {
            await cache.SetAsync(key, Payload(), CancellationToken.None);
        }

        if (manyCachedTiles)
        {
            // More tiles cached at the zoom than the range holds: the range is walked.
            for (var x = 0; x < 20; x++)
                await cache.SetAsync(Key(new TileAddress(12, x, 0)), Payload(), CancellationToken.None);
        }

        // A 3 × 3 range around the inside tile: fewer cached tiles at zoom 12 than its 9 tiles unless manyCachedTiles.
        await cache.RemoveAsync(new TileRange(12, 1502, 2229, 1504, 2231), CancellationToken.None);

        (await cache.GetAsync(Key(inside), CancellationToken.None)).Should().BeNull();
        (await cache.GetAsync(Key(inside, OrgB), CancellationToken.None)).Should().BeNull();
        (await cache.GetAsync(Key(inside, version: "v2"), CancellationToken.None)).Should().BeNull();
        (await cache.GetAsync(Key(inside, format: TileFormat.Png), CancellationToken.None)).Should().BeNull();
        (await cache.GetAsync(Key(outside), CancellationToken.None)).Should().NotBeNull();
        (await cache.GetAsync(Key(otherZoom), CancellationToken.None)).Should().NotBeNull();
        cache.Count.Should().Be(manyCachedTiles ? 22 : 2);
    }

    [Fact]
    public async Task Remove_AWorldSizedRange_AtADeepZoom_OnlyWalksTheCachedTiles()
    {
        var cache = new MemoryTileCache();
        var deep = new TileAddress(24, 8_000_000, 9_000_000);
        await cache.SetAsync(Key(deep), Payload(), CancellationToken.None);

        var whole = new TileRange(24, 0, 0, TileAddress.TilesPerAxis(24) - 1, TileAddress.TilesPerAxis(24) - 1);
        var removal = cache.RemoveAsync(whole, CancellationToken.None);

        removal.IsCompletedSuccessfully.Should().BeTrue("a 4^24-tile range is never enumerated");
        await removal;
        cache.Count.Should().Be(0);
    }

    [Fact]
    public async Task Remove_AtAZoomWithNothingCached_IsANoOp()
    {
        var cache = new MemoryTileCache();
        await cache.SetAsync(Key(), Payload(), CancellationToken.None);

        await cache.RemoveAsync(TileRange.Of(new TileAddress(3, 1, 1)), CancellationToken.None);
        await cache.RemoveAsync(TileRange.Of(new TileAddress(3, 1, 1)), CancellationToken.None);

        cache.Count.Should().Be(1);
    }

    [Fact]
    public async Task ConcurrentUse_KeepsTheBoundAndTheIndexesConsistent()
    {
        var cache = new MemoryTileCache(maxBytes: 50 * (64 + MemoryTileCache.EntryOverheadBytes));

        await Parallel.ForEachAsync(Enumerable.Range(0, 2000), async (i, ct) =>
        {
            var tile = new TileAddress(10, i % 97, i % 13);
            await cache.SetAsync(Key(tile, version: (i % 3).ToString(System.Globalization.CultureInfo.InvariantCulture)), Payload(64), ct);
            await cache.GetAsync(Key(tile), ct);
            if (i % 50 == 0) await cache.RemoveAsync(new TileRange(10, 0, 0, 40, 12), ct);
        });

        cache.Bytes.Should().BeLessThanOrEqualTo(cache.MaxBytes);
        cache.Bytes.Should().Be(cache.Count * (64L + MemoryTileCache.EntryOverheadBytes));
        await cache.RemoveAsync(new TileRange(10, 0, 0, 1023, 1023), CancellationToken.None);
        cache.Count.Should().Be(0);
        cache.Bytes.Should().Be(0);
    }

    [Fact]
    public void Construction_RefusesANonPositiveBound()
    {
        var act = () => new MemoryTileCache(0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Set_RefusesANullPayload()
    {
        Action act = () => _ = new MemoryTileCache().SetAsync(Key(), null!, CancellationToken.None);

        act.Should().Throw<ArgumentNullException>();
    }
}
