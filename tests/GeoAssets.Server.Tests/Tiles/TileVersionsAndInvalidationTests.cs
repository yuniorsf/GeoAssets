using FluentAssertions;
using GeoAssets.Core.Rendering;
using GeoAssets.Server.Tiles;
using Xunit;

namespace GeoAssets.Server.Tests.Tiles;

/// <summary>
/// XD01-434 — <see cref="TileVersions"/> (<c>{v}</c> = <c>{style}-{epoch}-{flush}.{generation}</c>) and
/// <see cref="TileInvalidation"/>: a committed write advances every <c>{v}</c>, evicts exactly its dirty tiles at the cached
/// zooms, and a write too large to evict tile by tile flushes every cache version instead.
/// </summary>
public sealed class TileVersionsAndInvalidationTests
{
    private const string Style = "0123456789ab";
    private static readonly TileCacheScope Scope = TileCacheScope.Create("org-a", []);

    // A ~100 m box near (-38.5, -13.0).
    private static readonly BoundingBox Change = new(-38.5005, -13.0005, -38.4995, -12.9995);

    // ── Versions ──────────────────────────────────────────────────────────────

    [Fact]
    public void PublishedVersion_IsTheCacheVersionAndTheGeneration()
    {
        var versions = new TileVersions("cafe0001");

        versions.CacheVersion(Style).Should().Be("0123456789ab-cafe0001-0");
        versions.PublishedVersion(Style).Should().Be("0123456789ab-cafe0001-0.0");
        TileKeySegment.IsValid(versions.PublishedVersion(Style)).Should().BeTrue("it is a URL and cache-key segment");

        versions.Advance();
        versions.CacheVersion(Style).Should().Be("0123456789ab-cafe0001-0", "an ordinary write keeps the cached tiles' version");
        versions.PublishedVersion(Style).Should().Be("0123456789ab-cafe0001-0.1");

        versions.Flush();
        versions.CacheVersion(Style).Should().Be("0123456789ab-cafe0001-1");
    }

    [Fact]
    public void TheDefaultEpoch_IsRandomPerProcess()
    {
        new TileVersions().Epoch.Should().MatchRegex("^[0-9a-f]{8}$");
        new TileVersions().Epoch.Should().NotBe(new TileVersions().Epoch);
    }

    [Theory]
    [InlineData("0123456789ab-cafe0001-0.0", true)]
    [InlineData("0123456789ab-cafe0001-0.2", true)]
    [InlineData("0123456789ab-cafe0001-0.3", false)]
    [InlineData("0123456789ab-beef0001-0.1", false)]
    [InlineData("ffffffffffff-cafe0001-0.1", false)]
    [InlineData("0123456789ab-cafe0001-1.1", false)]
    [InlineData("0123456789ab-cafe0001-0", false)]
    [InlineData("0123456789ab-cafe0001-0.-1", false)]
    [InlineData("0123456789ab-cafe0001-0.x", false)]
    [InlineData(".1", false)]
    [InlineData("v1", false)]
    public void IsCurrent_OnlyForTodaysCacheVersion_AtAGenerationNotAheadOfThisProcess(string version, bool current)
    {
        var versions = new TileVersions("cafe0001");
        versions.Advance();
        versions.Advance();

        versions.IsCurrent(version, Style).Should().Be(current);
    }

    // ── Invalidation ──────────────────────────────────────────────────────────

    [Fact]
    public async Task AWrite_AdvancesTheGeneration_AndEvictsExactlyItsDirtyTiles_AtEveryCachedZoom()
    {
        var (cache, versions, invalidation) = Create();
        var dirty = new[] { 8, 12, 16 }.Select(z => TileAddress.Containing(-38.5, -13.0, z)).ToArray();
        var clean = new[] { 8, 12, 16 }.Select(z => TileAddress.Containing(-40.5, -15.0, z)).ToArray();
        var tooDeep = TileAddress.Containing(-38.5, -13.0, 19);
        foreach (var tile in dirty.Concat(clean).Append(tooDeep))
            await cache.SetAsync(Key(tile, versions), Payload(), CancellationToken.None);

        await invalidation.InvalidateAsync([new DirtyTileRegion(Change)], CancellationToken.None);

        versions.Generation.Should().Be(1);
        versions.Flushes.Should().Be(0);
        foreach (var tile in dirty)
            (await cache.GetAsync(Key(tile, versions), CancellationToken.None)).Should().BeNull("{0} shows the change", tile);
        foreach (var tile in clean)
            (await cache.GetAsync(Key(tile, versions), CancellationToken.None)).Should().NotBeNull("{0} is away from the change", tile);
        (await cache.GetAsync(Key(tooDeep, versions), CancellationToken.None)).Should().NotBeNull(
            "zooms above MaxCachedZoom are never cached by the endpoint, so the invalidation does not walk them");
    }

    [Fact]
    public async Task AChangeNearATilesEdge_DirtiesTheNeighbourThatDrawsIt()
    {
        var (cache, versions, invalidation) = Create();
        var tile = new TileAddress(16, 30000, 35000);
        var neighbour = new TileAddress(16, 30001, 35000);
        var east = tile.Envelope4326.MaxLon;
        await cache.SetAsync(Key(neighbour, versions), Payload(), CancellationToken.None);

        // A point just inside the west tile, within the symbol buffer of its east neighbour.
        var lat = (tile.Envelope4326.MinLat + tile.Envelope4326.MaxLat) / 2;
        await invalidation.InvalidateAsync([new DirtyTileRegion(new BoundingBox(east - 1e-6, lat, east - 1e-6, lat))], CancellationToken.None);

        (await cache.GetAsync(Key(neighbour, versions), CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task AWriteTooLargeToEvictTileByTile_FlushesEveryCacheVersion_InsteadOfWalkingTiles()
    {
        var (cache, versions, invalidation) = Create(maxEvicted: 100);
        var before = versions.CacheVersion(Style);
        var tile = TileAddress.Containing(-38.5, -13.0, 12);
        await cache.SetAsync(Key(tile, versions), Payload(), CancellationToken.None);

        await invalidation.InvalidateAsync([new DirtyTileRegion(new BoundingBox(-47.5, -24, -45, -22))], CancellationToken.None);

        versions.Flushes.Should().Be(1);
        versions.Generation.Should().Be(1);
        versions.CacheVersion(Style).Should().NotBe(before, "nothing cached under the old version is served again");
        (await cache.GetAsync(new TileCacheKey(Scope, "assets", before, tile, TileFormat.Mvt), CancellationToken.None))
            .Should().NotBeNull("the old entries are orphaned, not walked; they age out of the bounded cache");
    }

    [Fact]
    public async Task ARegionAboveTheCachedZooms_StillAdvancesTheGeneration()
    {
        var (_, versions, invalidation) = Create();

        await invalidation.InvalidateAsync([new DirtyTileRegion(Change, minZoom: 20)], CancellationToken.None);

        versions.Generation.Should().Be(1);
        versions.Flushes.Should().Be(0);
    }

    [Fact]
    public async Task NoRegions_IsANoOp_AndNullIsRefused()
    {
        var (_, versions, invalidation) = Create();

        await invalidation.InvalidateAsync([], CancellationToken.None);
        var act = () => invalidation.InvalidateAsync(null!, CancellationToken.None);

        versions.Generation.Should().Be(0);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task InvalidatingTwice_IsIdempotent_ForTheCache()
    {
        var (cache, versions, invalidation) = Create();
        var tile = TileAddress.Containing(-38.5, -13.0, 12);
        await cache.SetAsync(Key(tile, versions), Payload(), CancellationToken.None);

        await invalidation.InvalidateAsync([new DirtyTileRegion(Change)], CancellationToken.None);
        await invalidation.InvalidateAsync([new DirtyTileRegion(Change)], CancellationToken.None);

        (await cache.GetAsync(Key(tile, versions), CancellationToken.None)).Should().BeNull();
        versions.Generation.Should().Be(2);
    }

    private static (MemoryTileCache Cache, TileVersions Versions, TileInvalidation Invalidation) Create(int maxEvicted = TileServingOptions.DefaultMaxEvictedTilesPerWrite)
    {
        var cache = new MemoryTileCache();
        var versions = new TileVersions("cafe0001");
        var options = TileServingOptions.Default with { MaxEvictedTilesPerWrite = maxEvicted };
        return (cache, versions, new TileInvalidation(cache, versions, options));
    }

    private static TileCacheKey Key(TileAddress tile, TileVersions versions) =>
        new(Scope, "assets", versions.CacheVersion(Style), tile, TileFormat.Mvt);

    private static TilePayload Payload() => new(new byte[] { 1, 2, 3 }, TileFormats.MapboxVectorTileContentType);
}
