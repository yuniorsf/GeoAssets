using FluentAssertions;
using GeoAssets.Core.Rendering;
using Xunit;

namespace GeoAssets.Core.Tests.Rendering;

/// <summary>XD01-431 — <see cref="DirtyTileRegion"/>: which tiles, at which zooms, a committed change dirties.</summary>
public class DirtyTileRegionTests
{
    private static readonly BoundingBox Pole = BoundingBox.Around(-38.5014, -12.9714, 0, 0);

    [Fact]
    public void Defaults_CoverEveryZoom()
    {
        var region = new DirtyTileRegion(Pole);

        region.Bounds.Should().Be(Pole);
        region.MinZoom.Should().Be(TileAddress.MinZoom);
        region.MaxZoom.Should().Be(TileAddress.MaxZoom);
        region.TileRanges().Should().HaveCount(25);
    }

    [Fact]
    public void TileRanges_OfAPoint_AreItsTileAndAncestorsAtEveryZoom()
    {
        var region = new DirtyTileRegion(Pole, 10, 16);

        var ranges = region.TileRanges().ToList();

        ranges.Select(r => r.Zoom).Should().Equal(10, 11, 12, 13, 14, 15, 16);
        ranges.Should().OnlyContain(r => r.Count == 1);
        ranges[^1].Should().Be(TileRange.Of(new TileAddress(16, 25759, 35149)));
        ranges[0].Should().Be(TileRange.Of(new TileAddress(10, 402, 549)));
        ranges.Should().OnlyContain(r => r.EnumerateTiles().Single() == new TileAddress(16, 25759, 35149).Ancestor(r.Zoom));
    }

    [Fact]
    public void TileRanges_OfABox_CoverItAtEachZoom()
    {
        var box = new BoundingBox(-38.9, -13.1, -38.4, -12.7);

        var ranges = new DirtyTileRegion(box, 9, 11).TileRanges().ToList();

        ranges.Should().Equal(TileRange.Covering(box, 9), TileRange.Covering(box, 10), TileRange.Covering(box, 11));
        ranges[1].Should().Be(new TileRange(10, 401, 548, 402, 549));
        ranges[2].Count.Should().BeGreaterThanOrEqualTo(ranges[1].Count);
    }

    [Fact]
    public void TileRanges_WithABuffer_IncludeTheNeighborsThatDrawTheChange()
    {
        var tile = new TileAddress(14, 6000, 9000);
        var box = tile.Envelope4326;
        var nearNorthEastCorner = BoundingBox.Around(
            box.MaxLon - ((box.MaxLon - box.MinLon) * 0.001), box.MaxLat - ((box.MaxLat - box.MinLat) * 0.001), 0, 0);

        var plain = new DirtyTileRegion(nearNorthEastCorner, 14, 14).TileRanges().Single();
        var buffered = new DirtyTileRegion(nearNorthEastCorner, 14, 14).TileRanges(64.0 / 4096).Single();

        plain.Should().Be(TileRange.Of(tile));
        buffered.Should().Be(new TileRange(14, 6000, 8999, 6001, 9000));
    }

    [Fact]
    public void TileRanges_AtTheAntimeridianAndPoles_StayInsideTheWorld()
    {
        var ranges = new DirtyTileRegion(new BoundingBox(179.9, 85, 180, 90), 0, 3).TileRanges(0.5).ToList();

        ranges.Should().Equal(
            new TileRange(0, 0, 0, 0, 0),
            new TileRange(1, 1, 0, 1, 0),
            new TileRange(2, 3, 0, 3, 0),
            new TileRange(3, 7, 0, 7, 0));
    }

    [Fact]
    public void TileRanges_AreLazy_SoAHugeRegionIsCheapUntilWalked()
    {
        var ranges = new DirtyTileRegion(new BoundingBox(-180, -90, 180, 90)).TileRanges();

        ranges.Last().Count.Should().Be(1L << 48);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    public void TileRanges_RefuseABadBuffer(double buffer)
    {
        var act = () => new DirtyTileRegion(Pole).TileRanges(buffer);

        act.Should().Throw<ArgumentOutOfRangeException>().Which.ParamName.Should().Be("buffer");
    }

    [Theory]
    [InlineData(-1, 5, "minZoom")]
    [InlineData(0, 25, "maxZoom")]
    [InlineData(6, 5, "minZoom")]
    public void Constructor_RefusesBadZooms(int minZoom, int maxZoom, string param)
    {
        var act = () => new DirtyTileRegion(Pole, minZoom, maxZoom);

        act.Should().Throw<ArgumentOutOfRangeException>().Which.ParamName.Should().Be(param);
    }

    [Fact]
    public void Constructor_RefusesABadBox()
    {
        var inverted = () => new DirtyTileRegion(new BoundingBox(1, 0, 0, 1));
        var notFinite = () => new DirtyTileRegion(new BoundingBox(0, 0, double.PositiveInfinity, 1));

        inverted.Should().Throw<ArgumentException>().Which.ParamName.Should().Be("bounds");
        notFinite.Should().Throw<ArgumentException>().Which.ParamName.Should().Be("bounds");
    }

    [Fact]
    public void Default_IsTheOriginAtZoomZero()
    {
        var region = default(DirtyTileRegion);

        region.TileRanges().Should().Equal(new TileRange(0, 0, 0, 0, 0));
    }
}
