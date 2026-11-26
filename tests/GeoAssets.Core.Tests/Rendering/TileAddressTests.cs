using FluentAssertions;
using GeoAssets.Core.Rendering;
using Xunit;

namespace GeoAssets.Core.Tests.Rendering;

/// <summary>XD01-431 — <see cref="TileAddress"/> validation and envelope math, against the OSM slippy-map formulas.</summary>
public class TileAddressTests
{
    private const double Half = 20037508.342789244;
    private const double MaxLat = 85.0511287798066;
    private const double Degrees = 1e-9;
    private const double Meters = 1e-6;

    // ── Validation ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(1, 1, 1)]
    [InlineData(10, 1023, 0)]
    [InlineData(24, 16_777_215, 16_777_215)]
    public void Constructor_AcceptsEveryTileOfThePyramid(int z, int x, int y)
    {
        var tile = new TileAddress(z, x, y);

        tile.Z.Should().Be(z);
        tile.X.Should().Be(x);
        tile.Y.Should().Be(y);
        TileAddress.IsValid(z, x, y).Should().BeTrue();
    }

    [Theory]
    [InlineData(-1, 0, 0, "z")]
    [InlineData(25, 0, 0, "z")]
    [InlineData(0, 1, 0, "x")]
    [InlineData(0, 0, 1, "y")]
    [InlineData(3, 8, 0, "x")]
    [InlineData(3, 0, 8, "y")]
    [InlineData(3, -1, 0, "x")]
    [InlineData(3, 0, -1, "y")]
    [InlineData(24, 16_777_216, 0, "x")]
    public void Constructor_RefusesWhatIsOutsideThePyramid(int z, int x, int y, string param)
    {
        var act = () => new TileAddress(z, x, y);

        act.Should().Throw<ArgumentOutOfRangeException>().Which.ParamName.Should().Be(param);
        TileAddress.IsValid(z, x, y).Should().BeFalse();
    }

    [Fact]
    public void Default_IsTheWorldTile()
    {
        default(TileAddress).Should().Be(new TileAddress(0, 0, 0));
    }

    [Fact]
    public void TryCreate_AnswersTheTileOrFalse()
    {
        TileAddress.TryCreate(2, 3, 1, out var tile).Should().BeTrue();
        tile.Should().Be(new TileAddress(2, 3, 1));

        TileAddress.TryCreate(2, 4, 1, out var none).Should().BeFalse();
        none.Should().Be(default(TileAddress));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(10, 1024)]
    [InlineData(24, 16_777_216)]
    public void TilesPerAxis_IsTwoToTheZoom(int zoom, int expected)
    {
        TileAddress.TilesPerAxis(zoom).Should().Be(expected);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(25)]
    public void TilesPerAxis_RefusesAZoomOutsideThePyramid(int zoom)
    {
        var act = () => TileAddress.TilesPerAxis(zoom);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Equality_IsByValue()
    {
        new TileAddress(5, 3, 7).Should().Be(new TileAddress(5, 3, 7));
        new TileAddress(5, 3, 7).Should().NotBe(new TileAddress(5, 7, 3));
        new TileAddress(5, 3, 7).GetHashCode().Should().Be(new TileAddress(5, 3, 7).GetHashCode());
    }

    [Fact]
    public void ToString_IsTheInvariantZxyPath()
    {
        new TileAddress(18, 232_792, 103_219).ToString().Should().Be("18/232792/103219");
    }

    // ── XYZ ↔ TMS ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(1, 0, 1)]
    [InlineData(1, 1, 0)]
    [InlineData(3, 2, 5)]
    public void TmsY_FlipsTheRow(int z, int y, int tmsY)
    {
        var tile = new TileAddress(z, 0, y);

        tile.TmsY.Should().Be(tmsY);
        TileAddress.FromTms(z, 0, tmsY).Should().Be(tile);
    }

    [Theory]
    [InlineData(2, 0, 4, "tmsY")]
    [InlineData(2, 0, -1, "tmsY")]
    [InlineData(2, 4, 0, "x")]
    [InlineData(25, 0, 0, "z")]
    public void FromTms_RefusesWhatIsOutsideThePyramid(int z, int x, int tmsY, string param)
    {
        var act = () => TileAddress.FromTms(z, x, tmsY);

        act.Should().Throw<ArgumentOutOfRangeException>().Which.ParamName.Should().Be(param);
    }

    // ── Envelopes (known pairs) ─────────────────────────────────────────────

    [Fact]
    public void Envelope_OfTheWorldTile_IsTheSquareWorld()
    {
        var tile = new TileAddress(0, 0, 0);

        ShouldBe(tile.Envelope3857, -Half, -Half, Half, Half);
        ShouldBe(tile.Envelope4326, -180, -MaxLat, 180, MaxLat);
    }

    [Theory]
    [InlineData(1, 0, 0, -Half, 0, 0, Half, -180, 0, 0, MaxLat)]
    [InlineData(1, 1, 1, 0, -Half, Half, 0, 0, -MaxLat, 180, 0)]
    [InlineData(2, 1, 1, -10018754.171394622, 0, 0, 10018754.171394622, -90, 0, 0, 66.51326044311186)]
    [InlineData(10, 402, 549, -4304933.433021126, -1487158.8223163895, -4265797.674539117, -1448023.0638343804,
        -38.671875, -13.239945499286305, -38.3203125, -12.897489183755896)]
    [InlineData(12, 1503, 2230, -5332247.093173895, -1790460.950551968, -5322463.153553393, -1780677.0109314658,
        -47.900390625, -15.876809064146764, -47.8125, -15.792253570362444)]
    [InlineData(15, 16592, 11272, 254382.43013306707, 6250714.425048573, 255605.42258562893, 6251937.417501137,
        2.28515625, 48.85748700264548, 2.296142578125, 48.86471476180278)]
    [InlineData(18, 232792, 103219, 15550349.034336258, 4257848.223597458, 15550501.908392828, 4258001.097654028,
        139.691162109375, 35.688533207388744, 139.69253540039062, 35.68964858696093)]
    public void Envelopes_MatchTheSlippyMapFormulas(
        int z, int x, int y,
        double minX, double minY, double maxX, double maxY,
        double minLon, double minLat, double maxLon, double maxLat)
    {
        var tile = new TileAddress(z, x, y);

        ShouldBe(tile.Envelope3857, minX, minY, maxX, maxY);
        ShouldBe(tile.Envelope4326, minLon, minLat, maxLon, maxLat);
    }

    [Fact]
    public void Envelope_AtTheHighestZoom_ReachesTheSouthEastCorner()
    {
        var corner = new TileAddress(24, 16_777_215, 16_777_215);

        ShouldBe(corner.Envelope4326, 179.99997854232788, -MaxLat, 180, -85.05112692872287);
        corner.Envelope3857.MaxX.Should().BeApproximately(Half, Meters);
        corner.Envelope3857.MinY.Should().BeApproximately(-Half, Meters);
        corner.Envelope3857.Width.Should().BeApproximately(2 * Half / 16_777_216, Meters);
    }

    [Fact]
    public void Envelopes_OfNeighbors_ShareTheirEdgeExactly()
    {
        var tile = new TileAddress(7, 40, 60);
        var east = new TileAddress(7, 41, 60);
        var south = new TileAddress(7, 40, 61);

        tile.Envelope3857.MaxX.Should().Be(east.Envelope3857.MinX);
        tile.Envelope3857.MinY.Should().Be(south.Envelope3857.MaxY);
    }

    [Fact]
    public void Envelope_IsSquareInMeters()
    {
        var bounds = new TileAddress(9, 170, 260).Envelope3857;

        bounds.Width.Should().BeApproximately(bounds.Height, Meters);
        bounds.Width.Should().BeApproximately(2 * Half / 512, Meters);
    }

    // ── Containing (point → tile, clamping) ─────────────────────────────────

    [Theory]
    [InlineData(-38.5014, -12.9714, 10, 402, 549)]
    [InlineData(-38.5014, -12.9714, 16, 25759, 35149)]
    [InlineData(-47.8825, -15.7942, 12, 1503, 2230)]
    [InlineData(2.2945, 48.8584, 15, 16592, 11272)]
    [InlineData(139.6917, 35.6895, 18, 232792, 103219)]
    [InlineData(-0.0001, 0.0001, 1, 0, 0)]
    [InlineData(0, 0, 1, 1, 1)]
    public void Containing_IsTheSlippyMapTile(double lon, double lat, int zoom, int x, int y)
    {
        TileAddress.Containing(lon, lat, zoom).Should().Be(new TileAddress(zoom, x, y));
    }

    [Theory]
    [InlineData(-180, 0, 0)]
    [InlineData(-200, 0, 0)]
    [InlineData(180, 0, 7)]
    [InlineData(540, 0, 7)]
    public void Containing_AtOrBeyondTheAntimeridian_ClampsToTheFirstOrLastColumn(double lon, double lat, int x)
    {
        TileAddress.Containing(lon, lat, 3).X.Should().Be(x);
    }

    [Theory]
    [InlineData(90, 0)]
    [InlineData(89.9, 0)]
    [InlineData(85.06, 0)]
    [InlineData(-90, 7)]
    [InlineData(-85.06, 7)]
    public void Containing_AtOrNearThePoles_ClampsToTheFirstOrLastRow(double lat, int y)
    {
        TileAddress.Containing(10, lat, 3).Y.Should().Be(y);
    }

    [Fact]
    public void Containing_TheWorld_AtZoomZero_IsTheWorldTile()
    {
        TileAddress.Containing(-180, 90, 0).Should().Be(new TileAddress(0, 0, 0));
        TileAddress.Containing(180, -90, 0).Should().Be(new TileAddress(0, 0, 0));
    }

    [Fact]
    public void Containing_TheCenterOfATile_RoundTripsItsEnvelope()
    {
        foreach (var tile in new[] { new TileAddress(4, 5, 9), new TileAddress(14, 6000, 9000), new TileAddress(24, 123_456, 7_654_321) })
        {
            var box = tile.Envelope4326;
            TileAddress.Containing((box.MinLon + box.MaxLon) / 2, (box.MinLat + box.MaxLat) / 2, tile.Z).Should().Be(tile);
        }
    }

    [Theory]
    [InlineData(double.NaN, 0)]
    [InlineData(0, double.NaN)]
    [InlineData(double.PositiveInfinity, 0)]
    [InlineData(0, double.NegativeInfinity)]
    public void Containing_RefusesANonFiniteCoordinate(double lon, double lat)
    {
        var act = () => TileAddress.Containing(lon, lat, 5);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Containing_RefusesAZoomOutsideThePyramid()
    {
        var act = () => TileAddress.Containing(0, 0, 25);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // ── Pyramid navigation (parent, ancestors, descendants, metatiles) ──────

    [Fact]
    public void Parent_IsTheTileOneZoomOut()
    {
        new TileAddress(3, 5, 6).Parent.Should().Be(new TileAddress(2, 2, 3));
        new TileAddress(1, 1, 0).Parent.Should().Be(new TileAddress(0, 0, 0));
    }

    [Fact]
    public void Parent_OfTheWorldTile_IsNull()
    {
        new TileAddress(0, 0, 0).Parent.Should().BeNull();
    }

    [Fact]
    public void Ancestor_IsTheTileAtThatZoomContainingThisOne()
    {
        var tile = new TileAddress(18, 232792, 103219);

        tile.Ancestor(18).Should().Be(tile);
        tile.Ancestor(10).Should().Be(new TileAddress(10, 909, 403));
        tile.Ancestor(0).Should().Be(new TileAddress(0, 0, 0));
        tile.Ancestor(10).Envelope4326.MinLon.Should().BeLessThanOrEqualTo(tile.Envelope4326.MinLon);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(6)]
    public void Ancestor_RefusesAZoomOutsideZeroToItsOwn(int zoom)
    {
        var act = () => new TileAddress(5, 1, 1).Ancestor(zoom);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Descendants_OneZoomIn_AreTheFourChildren()
    {
        var children = new TileAddress(3, 5, 6).Descendants(4);

        children.Should().Be(new TileRange(4, 10, 12, 11, 13));
        children.EnumerateTiles().Should().Equal(
            new TileAddress(4, 10, 12), new TileAddress(4, 11, 12), new TileAddress(4, 10, 13), new TileAddress(4, 11, 13));
        children.EnumerateTiles().Should().OnlyContain(child => child.Parent == new TileAddress(3, 5, 6));
    }

    [Fact]
    public void Descendants_SeveralZoomsIn_CoverTheTileExactly()
    {
        var tile = new TileAddress(2, 1, 3);
        var descendants = tile.Descendants(5);

        descendants.Should().Be(new TileRange(5, 8, 24, 15, 31));
        descendants.Count.Should().Be(64);
        tile.Descendants(2).Should().Be(TileRange.Of(tile));
        new TileAddress(0, 0, 0).Descendants(24).Count.Should().Be(1L << 48);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(25)]
    public void Descendants_RefusesAZoomOutsideItsOwnToTheHighest(int zoom)
    {
        var act = () => new TileAddress(3, 1, 1).Descendants(zoom);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Metatile_TwoByTwo_IsTheParentsChildren()
    {
        var tile = new TileAddress(6, 21, 34);

        tile.Metatile(2).Should().Be(new TileRange(6, 20, 34, 21, 35));
        tile.Metatile(2).Should().Be(tile.Parent!.Value.Descendants(6));
    }

    [Fact]
    public void Metatile_IsAlignedAndCutShortAtTheWorldsEdge()
    {
        new TileAddress(3, 7, 7).Metatile(4).Should().Be(new TileRange(3, 4, 4, 7, 7));
        new TileAddress(3, 2, 5).Metatile(3).Should().Be(new TileRange(3, 0, 3, 2, 5));
        new TileAddress(1, 1, 0).Metatile(8).Should().Be(new TileRange(1, 0, 0, 1, 1));
        new TileAddress(0, 0, 0).Metatile(2).Should().Be(new TileRange(0, 0, 0, 0, 0));
        new TileAddress(5, 9, 9).Metatile(1).Should().Be(TileRange.Of(new TileAddress(5, 9, 9)));
        new TileAddress(24, 16_777_215, 0).Metatile(int.MaxValue).Should().Be(new TileRange(24, 0, 0, 16_777_215, 16_777_215));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    public void Metatile_RefusesASpanBelowOne(int span)
    {
        var act = () => new TileAddress(3, 1, 1).Metatile(span);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static void ShouldBe(MercatorBounds actual, double minX, double minY, double maxX, double maxY)
    {
        actual.MinX.Should().BeApproximately(minX, Meters);
        actual.MinY.Should().BeApproximately(minY, Meters);
        actual.MaxX.Should().BeApproximately(maxX, Meters);
        actual.MaxY.Should().BeApproximately(maxY, Meters);
    }

    private static void ShouldBe(BoundingBox actual, double minLon, double minLat, double maxLon, double maxLat)
    {
        actual.MinLon.Should().BeApproximately(minLon, Degrees);
        actual.MinLat.Should().BeApproximately(minLat, Degrees);
        actual.MaxLon.Should().BeApproximately(maxLon, Degrees);
        actual.MaxLat.Should().BeApproximately(maxLat, Degrees);
    }
}
