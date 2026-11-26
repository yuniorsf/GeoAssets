using FluentAssertions;
using GeoAssets.Core.Rendering;
using Xunit;

namespace GeoAssets.Core.Tests.Rendering;

/// <summary>XD01-431 — <see cref="TileRange"/> validation and <see cref="TileRange.Covering"/>.</summary>
public class TileRangeTests
{
    [Fact]
    public void Constructor_KeepsTheBoundsAndCountsTheTiles()
    {
        var range = new TileRange(4, 2, 3, 5, 4);

        range.Zoom.Should().Be(4);
        (range.MinX, range.MinY, range.MaxX, range.MaxY).Should().Be((2, 3, 5, 4));
        range.Width.Should().Be(4);
        range.Height.Should().Be(2);
        range.Count.Should().Be(8);
    }

    [Theory]
    [InlineData(25, 0, 0, 0, 0, "zoom")]
    [InlineData(2, -1, 0, 0, 0, "minX")]
    [InlineData(2, 0, 0, 4, 0, "maxX")]
    [InlineData(2, 0, -1, 0, 0, "minY")]
    [InlineData(2, 0, 0, 0, 4, "maxY")]
    [InlineData(2, 3, 0, 2, 0, "minX")]
    [InlineData(2, 0, 3, 0, 2, "minY")]
    public void Constructor_RefusesBoundsOutsideTheGridOrInverted(int zoom, int minX, int minY, int maxX, int maxY, string param)
    {
        var act = () => new TileRange(zoom, minX, minY, maxX, maxY);

        act.Should().Throw<ArgumentOutOfRangeException>().Which.ParamName.Should().Be(param);
    }

    [Fact]
    public void Of_IsTheSingleTile()
    {
        var range = TileRange.Of(new TileAddress(9, 100, 200));

        range.Should().Be(new TileRange(9, 100, 200, 100, 200));
        range.Count.Should().Be(1);
        range.EnumerateTiles().Should().Equal(new TileAddress(9, 100, 200));
    }

    [Fact]
    public void EnumerateTiles_GoesRowByRowFromTheNorthWest()
    {
        new TileRange(3, 1, 4, 3, 5).EnumerateTiles().Should().Equal(
            new TileAddress(3, 1, 4), new TileAddress(3, 2, 4), new TileAddress(3, 3, 4),
            new TileAddress(3, 1, 5), new TileAddress(3, 2, 5), new TileAddress(3, 3, 5));
    }

    [Fact]
    public void Contains_IsTheTilesOfTheRangeAtItsZoom()
    {
        var range = new TileRange(5, 10, 10, 12, 11);

        range.Contains(new TileAddress(5, 10, 10)).Should().BeTrue();
        range.Contains(new TileAddress(5, 12, 11)).Should().BeTrue();
        range.Contains(new TileAddress(5, 13, 11)).Should().BeFalse();
        range.Contains(new TileAddress(5, 9, 10)).Should().BeFalse();
        range.Contains(new TileAddress(5, 11, 12)).Should().BeFalse();
        range.Contains(new TileAddress(5, 11, 9)).Should().BeFalse();
        range.Contains(new TileAddress(6, 11, 10)).Should().BeFalse();
    }

    [Fact]
    public void ToString_IsInvariant()
    {
        new TileRange(12, 1503, 2230, 1504, 2231).ToString().Should().Be("12/1503-1504/2230-2231");
    }

    // ── Covering ────────────────────────────────────────────────────────────

    [Fact]
    public void Covering_APoint_IsTheTileContainingIt()
    {
        var range = TileRange.Covering(BoundingBox.Around(-38.5014, -12.9714, 0, 0), 16);

        range.Should().Be(TileRange.Of(new TileAddress(16, 25759, 35149)));
    }

    [Fact]
    public void Covering_ABox_IsEveryTileItMeets()
    {
        // Salvador's bay, z10: columns 401..402, rows 548..549 — matches TileAddress.Containing at the corners.
        var bounds = new BoundingBox(-38.9, -13.1, -38.4, -12.7);

        var range = TileRange.Covering(bounds, 10);

        range.Should().Be(new TileRange(
            10,
            TileAddress.Containing(-38.9, -12.7, 10).X, TileAddress.Containing(-38.9, -12.7, 10).Y,
            TileAddress.Containing(-38.4, -13.1, 10).X, TileAddress.Containing(-38.4, -13.1, 10).Y));
        range.Should().Be(new TileRange(10, 401, 548, 402, 549));
    }

    [Fact]
    public void Covering_TheTilesOwnEnvelope_ReachesTheTouchingNeighbors()
    {
        // Edges inclusive: a box exactly a tile's envelope also meets the tiles east and south of it.
        var tile = new TileAddress(6, 20, 30);

        var range = TileRange.Covering(tile.Envelope4326, 6);

        range.Contains(tile).Should().BeTrue();
        range.MaxX.Should().BeLessThanOrEqualTo(21);
        range.MaxY.Should().BeLessThanOrEqualTo(31);
        range.MinX.Should().BeGreaterThanOrEqualTo(19);
        range.MinY.Should().BeGreaterThanOrEqualTo(29);
    }

    [Fact]
    public void Covering_TheWholeWorld_IsEveryTile()
    {
        TileRange.Covering(new BoundingBox(-180, -90, 180, 90), 0).Should().Be(new TileRange(0, 0, 0, 0, 0));
        TileRange.Covering(new BoundingBox(-180, -90, 180, 90), 3).Should().Be(new TileRange(3, 0, 0, 7, 7));
        TileRange.Covering(new BoundingBox(-180, -90, 180, 90), 24).Count.Should().Be(1L << 48);
    }

    [Fact]
    public void Covering_BeyondTheWorld_ClampsToItsEdge()
    {
        TileRange.Covering(new BoundingBox(170, 80, 400, 100), 3).Should().Be(new TileRange(3, 7, 0, 7, 0));
        TileRange.Covering(new BoundingBox(-400, -100, -170, -80), 3).Should().Be(new TileRange(3, 0, 7, 0, 7));
    }

    [Fact]
    public void Covering_TouchingTheAntimeridian_EndsAtTheLastColumn()
    {
        TileRange.Covering(new BoundingBox(179, 0.5, 180, 1), 4).Should().Be(new TileRange(4, 15, 7, 15, 7));
        TileRange.Covering(new BoundingBox(-180, 0.5, -179, 1), 4).Should().Be(new TileRange(4, 0, 7, 0, 7));
    }

    [Fact]
    public void Covering_ABoxEdgeOnATileBoundary_MeetsTheTileBeyondIt()
    {
        // Edges inclusive: the equator is the north edge of row 8 at zoom 4, so a box reaching it meets row 8 too.
        TileRange.Covering(new BoundingBox(10, 0, 11, 1), 4).Should().Be(new TileRange(4, 8, 7, 8, 8));
    }

    [Fact]
    public void Covering_WithABuffer_ReachesTheNeighborsOfAPointNearAnEdge()
    {
        var tile = new TileAddress(10, 402, 549);
        var box = tile.Envelope4326;
        var nearWestEdge = BoundingBox.Around(box.MinLon + ((box.MaxLon - box.MinLon) * 0.01), (box.MinLat + box.MaxLat) / 2, 0, 0);

        TileRange.Covering(nearWestEdge, 10).Should().Be(TileRange.Of(tile));
        TileRange.Covering(nearWestEdge, 10, 64.0 / 4096).Should().Be(new TileRange(10, 401, 549, 402, 549));
        TileRange.Covering(nearWestEdge, 10, 1).Should().Be(new TileRange(10, 401, 548, 403, 550));
    }

    [Fact]
    public void Covering_WithABuffer_StaysInsideTheWorld()
    {
        TileRange.Covering(BoundingBox.Around(0, 0, 0, 0), 1, 5).Should().Be(new TileRange(1, 0, 0, 1, 1));
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Covering_RefusesABadBuffer(double buffer)
    {
        var act = () => TileRange.Covering(new BoundingBox(0, 0, 1, 1), 5, buffer);

        act.Should().Throw<ArgumentOutOfRangeException>().Which.ParamName.Should().Be("buffer");
    }

    [Fact]
    public void Covering_RefusesAnInvertedBox()
    {
        var acrossTheAntimeridian = () => TileRange.Covering(new BoundingBox(170, 0, -170, 1), 5);
        var upsideDown = () => TileRange.Covering(new BoundingBox(0, 1, 1, 0), 5);

        acrossTheAntimeridian.Should().Throw<ArgumentException>().Which.ParamName.Should().Be("bounds");
        upsideDown.Should().Throw<ArgumentException>().Which.ParamName.Should().Be("bounds");
    }

    [Fact]
    public void Covering_RefusesANonFiniteBox()
    {
        var act = () => TileRange.Covering(new BoundingBox(double.NaN, 0, 1, 1), 5);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Covering_RefusesAZoomOutsideThePyramid()
    {
        var act = () => TileRange.Covering(new BoundingBox(0, 0, 1, 1), 25);

        act.Should().Throw<ArgumentOutOfRangeException>().Which.ParamName.Should().Be("zoom");
    }
}
