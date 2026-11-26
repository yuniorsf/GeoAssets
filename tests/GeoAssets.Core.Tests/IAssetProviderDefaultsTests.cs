using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Rendering;
using Xunit;

namespace GeoAssets.Core.Tests;

/// <summary>
/// Exercises <see cref="IAssetProvider"/>'s own default-implemented members —
/// <see cref="IAssetProvider.GetInBoundsRawJsonChunksAsync"/> here — via
/// <see cref="TestAssetProvider"/>, which deliberately does not override it (XD01-172).
/// Callers must be typed as <see cref="IAssetProvider"/>, not the concrete class: default
/// interface members are only reachable through the interface type.
/// </summary>
public class IAssetProviderDefaultsTests
{
    [Fact]
    public async Task GetInBoundsRawJsonChunksAsync_ProviderDoesNotSupportRawJson_YieldsNoChunks()
    {
        IAssetProvider sut = new TestAssetProvider(); // RawJsonResult left null — "unsupported", same as Postgres today

        var chunks = new List<string>();
        await foreach (var chunk in sut.GetInBoundsRawJsonChunksAsync(0, 0, 1, 1))
            chunks.Add(chunk);

        chunks.Should().BeEmpty();
    }

    [Fact]
    public async Task GetInBoundsRawJsonChunksAsync_ProviderSupportsRawJson_YieldsExactlyOneChunk()
    {
        IAssetProvider sut = new TestAssetProvider { RawJsonResult = """[{"id":"a"}]""" };

        var chunks = new List<string>();
        await foreach (var chunk in sut.GetInBoundsRawJsonChunksAsync(0, 0, 1, 1))
            chunks.Add(chunk);

        chunks.Should().ContainSingle().Which.Should().Be("""[{"id":"a"}]""");
    }

    [Fact]
    public async Task GetInBoundsRawJsonChunksAsync_EmptyButSupportedResult_StillYieldsOneChunk()
    {
        // A "[]" result is non-null — proves the default distinguishes "unsupported" (null) from
        // "supported but this bbox happens to be empty" (a real, if trivial, chunk).
        IAssetProvider sut = new TestAssetProvider { RawJsonResult = "[]" };

        var chunks = new List<string>();
        await foreach (var chunk in sut.GetInBoundsRawJsonChunksAsync(0, 0, 1, 1))
            chunks.Add(chunk);

        chunks.Should().ContainSingle().Which.Should().Be("[]");
    }

    // ── GetInViewportAsync (XD01-430) ────────────────────────────────────────

    private static readonly AssetType PoleType = new() { Name = "pole", MinZoom = 15 };
    private static readonly AssetType DistrictType = new() { Name = "district", MinZoom = 8, MaxZoom = 13 };

    private static GeoFeature Feature(string id, AssetType? type, double lon = 0.5, double lat = 0.5, string? assetTypeId = null) => new()
    {
        Id = id,
        Geometry = new GeoPoint(lon, lat),
        Properties = new GeoFeatureProperties { AssetTypeId = assetTypeId ?? type?.Id.ToString() ?? string.Empty },
    };

    private static TestAssetProvider ViewportWorld()
    {
        var provider = new TestAssetProvider();
        provider.AddAssetType(PoleType);
        provider.AddAssetType(DistrictType);
        provider.Add(Feature("pole-b", PoleType));
        provider.Add(Feature("pole-a", PoleType));
        provider.Add(Feature("district", DistrictType));
        provider.Add(Feature("untyped", null, assetTypeId: "not-a-stored-type"));
        provider.Add(Feature("outside", DistrictType, lon: 5, lat: 5));
        return provider;
    }

    private static async Task<string[]> ViewportIdsAsync(IAssetProvider sut, int? zoom, int maxCount = 100) =>
        [.. (await sut.GetInViewportAsync(new ViewportQuery(new BoundingBox(0, 0, 1, 1), zoom), maxCount)).Select(f => f.Id)];

    [Theory]
    [InlineData(7, new[] { "untyped" })]
    [InlineData(12, new[] { "district", "untyped" })]
    [InlineData(14, new[] { "untyped" })]
    [InlineData(16, new[] { "pole-a", "pole-b", "untyped" })]
    public async Task GetInViewportAsync_Zoom_LeavesOutTheTypesItHides_KeepsUnknownTypes_InOrdinalIdOrder(int zoom, string[] expected)
    {
        (await ViewportIdsAsync(ViewportWorld(), zoom)).Should().Equal(expected);
    }

    [Fact]
    public async Task GetInViewportAsync_NoZoom_ReturnsEveryFeatureInTheBounds()
    {
        (await ViewportIdsAsync(ViewportWorld(), zoom: null)).Should().Equal("district", "pole-a", "pole-b", "untyped");
    }

    [Fact]
    public async Task GetInViewportAsync_MaxCount_TakesTheFirstByOrdinalId_AfterTheZoomFilter()
    {
        var sut = ViewportWorld();

        (await ViewportIdsAsync(sut, zoom: 16, maxCount: 2)).Should().Equal("pole-a", "pole-b");
        (await ViewportIdsAsync(sut, zoom: null, maxCount: 1)).Should().Equal("district");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public async Task GetInViewportAsync_NonPositiveMaxCount_ReturnsNothing(int maxCount)
    {
        (await ViewportIdsAsync(ViewportWorld(), zoom: 16, maxCount)).Should().BeEmpty();
    }

    [Fact]
    public async Task GetInViewportAsync_AlreadyCancelled_RefusesToStart()
    {
        IAssetProvider sut = ViewportWorld();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => sut.GetInViewportAsync(new ViewportQuery(new BoundingBox(0, 0, 1, 1), 12), 10, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ── ViewportQuery reads (XD01-429) ───────────────────────────────────────

    private static readonly ViewportQuery Viewport = new(new BoundingBox(0, 0, 1, 1), 12);

    [Fact]
    public async Task GetInBoundsJsonAsync_Viewport_ForwardsItsBoundsToTheBboxRead()
    {
        var provider = new TestAssetProvider();
        provider.Add(new GeoFeature { Id = "in", Geometry = new GeoPoint(0.5, 0.5) });
        provider.Add(new GeoFeature { Id = "out", Geometry = new GeoPoint(5, 5) });
        IAssetProvider sut = provider;

        var features = await sut.GetInBoundsJsonAsync(Viewport);

        features.Select(f => f.GetProperty("id").GetString()).Should().Equal("in");
    }

    [Fact]
    public async Task GetInBoundsJsonAsync_Viewport_AlreadyCancelled_RefusesToStart()
    {
        IAssetProvider sut = new TestAssetProvider();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => sut.GetInBoundsJsonAsync(Viewport, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task GetInBoundsRawJsonChunksAsync_Viewport_ForwardsItsBoundsToTheBboxRead()
    {
        IAssetProvider sut = new TestAssetProvider { RawJsonResult = "[]" };

        var chunks = new List<string>();
        await foreach (var chunk in sut.GetInBoundsRawJsonChunksAsync(Viewport))
            chunks.Add(chunk);

        chunks.Should().Equal("[]");
    }

    // ── IsWithinDistanceAsync (XD01-221) ─────────────────────────────────────

    [Fact]
    public async Task IsWithinDistanceAsync_PointNearTheFeature_ReturnsTrue()
    {
        IAssetProvider sut = new TestAssetProvider();
        sut.Add(new GeoFeature { Id = "asset-1", Geometry = new GeoPoint(-46.633, -23.55) });

        var result = await sut.IsWithinDistanceAsync("asset-1", new GeoPoint(-46.633, -23.55), toleranceMeters: 30);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsWithinDistanceAsync_PointFarFromTheFeature_ReturnsFalse()
    {
        IAssetProvider sut = new TestAssetProvider();
        sut.Add(new GeoFeature { Id = "asset-1", Geometry = new GeoPoint(-46.633, -23.55) });

        // ~1 degree of latitude is on the order of 110km — nowhere near a 30m tolerance.
        var result = await sut.IsWithinDistanceAsync("asset-1", new GeoPoint(-46.633, -22.55), toleranceMeters: 30);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsWithinDistanceAsync_UnknownFeatureId_ReturnsFalse()
    {
        IAssetProvider sut = new TestAssetProvider();

        var result = await sut.IsWithinDistanceAsync("does-not-exist", new GeoPoint(0, 0), toleranceMeters: 30);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsWithinDistanceAsync_FeatureWithNoGeometry_ReturnsFalse()
    {
        IAssetProvider sut = new TestAssetProvider();
        sut.Add(new GeoFeature { Id = "asset-1", Geometry = null });

        var result = await sut.IsWithinDistanceAsync("asset-1", new GeoPoint(0, 0), toleranceMeters: 30);

        result.Should().BeFalse();
    }
}
