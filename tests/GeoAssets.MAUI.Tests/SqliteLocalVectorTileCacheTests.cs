using FluentAssertions;
using GeoAssets.Core.Rendering;
using GeoAssets.MAUI.Services.Offline;
using GeoAssets.Provider.PMTiles.Packs;
using GeoAssets.Provider.PMTiles.Tests;
using GeoAssets.Shared.Services.Offline;
using Microsoft.Extensions.Time.Testing;
using SQLite;
using Xunit;

namespace GeoAssets.MAUI.Tests;

/// <summary>
/// XD01-438 — <see cref="SqliteLocalVectorTileCache"/> as the app's <see cref="ITileCache"/> (the tiles of an offline map
/// pack read through it) beside its z/x/y basemap entries, over a real SQLite file, and an offline pack read through it
/// end to end.
/// </summary>
public sealed class SqliteLocalVectorTileCacheTests : IAsyncLifetime
{
    private static readonly TileCacheScope Scope = new("offline-pack", "pmtiles");

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "geoassets-maui-tests-" + Guid.NewGuid().ToString("N"));
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 27, 12, 0, 0, TimeSpan.Zero));
    private SQLiteAsyncConnection _db = null!;

    public Task InitializeAsync()
    {
        SqlCipherDatabase.EnsureProvider();
        Directory.CreateDirectory(_directory);
        _db = new SQLiteAsyncConnection(Path.Combine(_directory, "offline.db3"), storeDateTimeAsTicks: true);
        return _db.CreateTableAsync<LocalVectorTileRow>();
    }

    public async Task DisposeAsync()
    {
        await _db.CloseAsync();
        Directory.Delete(_directory, recursive: true);
    }

    private SqliteLocalVectorTileCache Cache(int maxTileEntries = SqliteLocalVectorTileCache.MaxTileEntries, int pruneInterval = 1) =>
        new(() => Task.FromResult(_db), _clock, maxTileEntries, pruneInterval);

    private static TileCacheKey Key(int z, int x, int y, string version = "v1") => new(Scope, "pmtiles", version, new TileAddress(z, x, y), TileFormat.Mvt);

    [Fact]
    public async Task ATileEntry_RoundTripsItsBytesTypeAndEncoding()
    {
        var cache = Cache();
        var payload = new TilePayload(new byte[] { 1, 2, 3 }, TileFormats.MapboxVectorTileContentType, "gzip");

        await cache.SetAsync(Key(3, 4, 2), payload, CancellationToken.None);
        var read = await cache.GetAsync(Key(3, 4, 2), CancellationToken.None);

        read!.Content.ToArray().Should().Equal(1, 2, 3);
        read.ContentType.Should().Be(TileFormats.MapboxVectorTileContentType);
        read.ContentEncoding.Should().Be("gzip");
    }

    [Fact]
    public async Task AnEmptyTile_IsCachedAsEmpty_AndAnUncompressedOneWithoutEncoding()
    {
        var cache = Cache();

        await cache.SetAsync(Key(1, 0, 0), TilePayload.Empty(TileFormats.MapboxVectorTileContentType), CancellationToken.None);
        await cache.SetAsync(Key(1, 1, 0), new TilePayload(new byte[] { 9 }, TileFormats.MapboxVectorTileContentType), CancellationToken.None);

        (await cache.GetAsync(Key(1, 0, 0), CancellationToken.None))!.IsEmpty.Should().BeTrue();
        (await cache.GetAsync(Key(1, 1, 0), CancellationToken.None))!.ContentEncoding.Should().BeNull();
    }

    [Fact]
    public async Task AMissingTile_IsNull_AndAnotherVersionIsAnotherEntry()
    {
        var cache = Cache();
        await cache.SetAsync(Key(2, 1, 1, "v1"), new TilePayload(new byte[] { 1 }, TileFormats.MapboxVectorTileContentType), CancellationToken.None);

        (await cache.GetAsync(Key(2, 1, 1, "v2"), CancellationToken.None)).Should().BeNull();
        (await cache.GetAsync(Key(2, 1, 2, "v1"), CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task TileEntries_AndBasemapEntries_ShareTheTableWithoutColliding()
    {
        var cache = Cache();
        var basemap = cache;
        await basemap.PutAsync(new LocalVectorTile(3, 4, 2, [7, 7], _clock.GetUtcNow().UtcDateTime));

        await cache.SetAsync(Key(3, 4, 2), new TilePayload(new byte[] { 1 }, TileFormats.MapboxVectorTileContentType), CancellationToken.None);

        (await basemap.GetAsync(3, 4, 2))!.Data.Should().Equal(7, 7);
        (await cache.GetAsync(Key(3, 4, 2), CancellationToken.None))!.Content.ToArray().Should().Equal(1);
        (await _db.Table<LocalVectorTileRow>().CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task RemovingARange_EvictsItsTileEntriesOnly()
    {
        var cache = Cache();
        var basemap = cache;
        await basemap.PutAsync(new LocalVectorTile(4, 5, 5, [7], _clock.GetUtcNow().UtcDateTime));
        foreach (var (x, y) in new[] { (5, 5), (6, 5), (9, 9) })
            await cache.SetAsync(Key(4, x, y), new TilePayload(new byte[] { 1 }, TileFormats.MapboxVectorTileContentType), CancellationToken.None);

        await cache.RemoveAsync(new TileRange(4, 5, 5, 6, 6), CancellationToken.None);

        (await cache.GetAsync(Key(4, 5, 5), CancellationToken.None)).Should().BeNull();
        (await cache.GetAsync(Key(4, 6, 5), CancellationToken.None)).Should().BeNull();
        (await cache.GetAsync(Key(4, 9, 9), CancellationToken.None)).Should().NotBeNull();
        (await basemap.GetAsync(4, 5, 5)).Should().NotBeNull("a basemap entry is not a tile cache entry");
    }

    [Fact]
    public async Task RemovingALayerVersion_EvictsExactlyItsTiles()
    {
        // XD01-464: a deleted offline map pack's tiles (version = the pack's id) go; another version's, even one the
        // version is a prefix of or matches as a LIKE pattern ('_'), and the basemap entries stay.
        var cache = Cache();
        var basemap = cache;
        await basemap.PutAsync(new LocalVectorTile(4, 5, 5, [7], _clock.GetUtcNow().UtcDateTime));
        foreach (var version in new[] { "a_c", "abc", "a_cd" })
        {
            await cache.SetAsync(Key(4, 5, 5, version), new TilePayload(new byte[] { 1 }, TileFormats.MapboxVectorTileContentType), CancellationToken.None);
            await cache.SetAsync(Key(9, 3, 1, version), new TilePayload(new byte[] { 2 }, TileFormats.MapboxVectorTileContentType), CancellationToken.None);
        }

        await cache.RemoveLayerVersionAsync(Scope, "pmtiles", "a_c", CancellationToken.None);

        (await cache.GetAsync(Key(4, 5, 5, "a_c"), CancellationToken.None)).Should().BeNull();
        (await cache.GetAsync(Key(9, 3, 1, "a_c"), CancellationToken.None)).Should().BeNull();
        (await cache.GetAsync(Key(4, 5, 5, "abc"), CancellationToken.None)).Should().NotBeNull();
        (await cache.GetAsync(Key(4, 5, 5, "a_cd"), CancellationToken.None)).Should().NotBeNull();
        (await cache.GetAsync(Key(9, 3, 1, "abc"), CancellationToken.None)).Should().NotBeNull();
        (await basemap.GetAsync(4, 5, 5)).Should().NotBeNull("a basemap entry is not a tile cache entry");

        await cache.RemoveLayerVersionAsync(Scope, "pmtiles", "a_c", CancellationToken.None);
        (await cache.GetAsync(Key(4, 5, 5, "abc"), CancellationToken.None)).Should().NotBeNull("evicting again is a no-op");
    }

    [Fact]
    public async Task PastItsLimit_TheOldestWrittenTileEntriesArePruned_BasemapEntriesKept()
    {
        var cache = Cache(maxTileEntries: 2, pruneInterval: 1);
        var basemap = cache;
        await basemap.PutAsync(new LocalVectorTile(0, 0, 0, [7], _clock.GetUtcNow().UtcDateTime.AddYears(-1)));
        for (var x = 0; x < 4; x++)
        {
            await cache.SetAsync(Key(2, x, 0), new TilePayload(new byte[] { (byte)x }, TileFormats.MapboxVectorTileContentType), CancellationToken.None);
            _clock.Advance(TimeSpan.FromMinutes(1));
        }

        (await cache.GetAsync(Key(2, 0, 0), CancellationToken.None)).Should().BeNull();
        (await cache.GetAsync(Key(2, 1, 0), CancellationToken.None)).Should().BeNull();
        (await cache.GetAsync(Key(2, 2, 0), CancellationToken.None)).Should().NotBeNull();
        (await cache.GetAsync(Key(2, 3, 0), CancellationToken.None)).Should().NotBeNull();
        (await basemap.GetAsync(0, 0, 0)).Should().NotBeNull();
    }

    [Fact]
    public async Task TheSizeIsChecked_OnlyEveryPruneInterval()
    {
        var cache = Cache(maxTileEntries: 1, pruneInterval: 3);

        for (var x = 0; x < 2; x++)
            await cache.SetAsync(Key(2, x, 0), new TilePayload(new byte[] { 1 }, TileFormats.MapboxVectorTileContentType), CancellationToken.None);
        (await _db.Table<LocalVectorTileRow>().CountAsync()).Should().Be(2);

        await cache.SetAsync(Key(2, 2, 0), new TilePayload(new byte[] { 1 }, TileFormats.MapboxVectorTileContentType), CancellationToken.None);
        (await _db.Table<LocalVectorTileRow>().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task ACancelledCall_StopsBeforeItsStatement()
    {
        var cache = Cache();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var act = () => cache.SetAsync(Key(1, 0, 0), new TilePayload(new byte[] { 1 }, TileFormats.MapboxVectorTileContentType), cancelled.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        (await _db.Table<LocalVectorTileRow>().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task AnOfflinePack_IsReadThroughTheSqliteCache_EachTileFromThePackOnce()
    {
        var archive = new PMTilesTestArchive { MaxZoom = 1, MetadataJson = """{"vector_layers":[{"id":"pois"}]}""" };
        foreach (var tile in new TileRange(1, 0, 0, 1, 1).EnumerateTiles())
            archive.Add(tile, PMTilesTestArchive.Gzip(TestMvt.Encode(new TestMvt.Layer("pois", [TestMvt.PointAt(2048, 2048)]))));
        using var pack = await PMTilesPack.OpenAsync(archive.WriteTo(_directory), CancellationToken.None);
        using var provider = new PMTilesPackAssetProvider(pack, Cache());

        var first = await provider.ReadAsync(new BoundingBox(-180, -85, 180, 85), mapZoom: 2);
        var rows = await _db.Table<LocalVectorTileRow>().ToListAsync();

        first.Should().HaveCount(4);
        rows.Should().HaveCount(4).And.OnlyContain(r => r.Key.StartsWith($"tile/offline-pack/pmtiles/pmtiles/{pack.Id}/1/", StringComparison.Ordinal));
        rows.Should().OnlyContain(r => string.Equals(r.ContentEncoding, "gzip", StringComparison.Ordinal));

        // A cached tile now holding two points proves the next read comes from the cache, not from the pack.
        var cached = rows.Single(r => r.X == 0 && r.Y == 0);
        cached.Data = PMTilesTestArchive.Gzip(TestMvt.Encode(new TestMvt.Layer("pois", [TestMvt.PointAt(100, 100), TestMvt.PointAt(200, 200)])));
        await _db.UpdateAsync(cached);

        var second = await provider.ReadAsync(new BoundingBox(-180, -85, 180, 85), mapZoom: 2);

        second.Should().HaveCount(5);
        (await _db.Table<LocalVectorTileRow>().CountAsync()).Should().Be(4);
    }
}
