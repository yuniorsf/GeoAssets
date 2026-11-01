using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
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
