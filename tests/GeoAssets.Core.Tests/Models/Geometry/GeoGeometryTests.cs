using FluentAssertions;
using GeoAssets.Core.Models.Geometry;
using NetTopologySuite.Geometries;
using Xunit;

namespace GeoAssets.Core.Tests.Models.Geometry;

public class GeoGeometryTests
{
    private static GeoPolygon BigBox() => new([
        (-10.0, -10.0), (10.0, -10.0), (10.0, 10.0), (-10.0, 10.0), (-10.0, -10.0)
    ]);

    private static GeoPolygon SmallBox() => new([
        (-1.0, -1.0), (1.0, -1.0), (1.0, 1.0), (-1.0, 1.0), (-1.0, -1.0)
    ]);

    // ── Srid ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Srid_DefaultWgs84Point_Returns4326()
    {
        new GeoPoint(0.0, 0.0).Srid.Should().Be(4326);
    }

    // ── GetBoundingBox ────────────────────────────────────────────────────────

    [Fact]
    public void GetBoundingBox_EmptyGeometry_ReturnsZeroes()
    {
        new GeoLineString().GetBoundingBox().Should().Equal(0.0, 0.0, 0.0, 0.0);
    }

    [Fact]
    public void GetBoundingBox_Point_ReturnsSinglePointExtent()
    {
        new GeoPoint(3.0, 7.0).GetBoundingBox().Should().Equal(3.0, 7.0, 3.0, 7.0);
    }

    [Fact]
    public void GetBoundingBox_Polygon_ReturnsCorrectExtent()
    {
        BigBox().GetBoundingBox().Should().Equal(-10.0, -10.0, 10.0, 10.0);
    }

    // ── Spatial predicates ────────────────────────────────────────────────────

    [Fact]
    public void Contains_SmallBoxInsideBigBox_ReturnsTrue()
    {
        BigBox().Contains(SmallBox()).Should().BeTrue();
    }

    [Fact]
    public void Contains_DisjointGeometry_ReturnsFalse()
    {
        BigBox().Contains(new GeoPoint(20.0, 20.0)).Should().BeFalse();
    }

    [Fact]
    public void Intersects_OverlappingGeometries_ReturnsTrue()
    {
        BigBox().Intersects(SmallBox()).Should().BeTrue();
    }

    [Fact]
    public void Intersects_DisjointGeometries_ReturnsFalse()
    {
        BigBox().Intersects(new GeoPoint(20.0, 20.0)).Should().BeFalse();
    }

    [Fact]
    public void Crosses_LineCrossingPolygonBoundary_ReturnsTrue()
    {
        new GeoLineString([(-20.0, 0.0), (20.0, 0.0)]).Crosses(BigBox()).Should().BeTrue();
    }

    [Fact]
    public void Crosses_LineEntirelyInsidePolygon_ReturnsFalse()
    {
        new GeoLineString([(-1.0, 0.0), (1.0, 0.0)]).Crosses(BigBox()).Should().BeFalse();
    }

    [Fact]
    public void Overlaps_PartiallyOverlappingPolygons_ReturnsTrue()
    {
        var box1 = new GeoPolygon([(-2.0, -2.0), (2.0, -2.0), (2.0, 2.0), (-2.0, 2.0), (-2.0, -2.0)]);
        var box2 = new GeoPolygon([(1.0, -2.0), (4.0, -2.0), (4.0, 2.0), (1.0, 2.0), (1.0, -2.0)]);
        box1.Overlaps(box2).Should().BeTrue();
    }

    [Fact]
    public void Overlaps_ContainedGeometry_ReturnsFalse()
    {
        BigBox().Overlaps(SmallBox()).Should().BeFalse();
    }

    [Fact]
    public void Touches_PointOnPolygonBoundary_ReturnsTrue()
    {
        new GeoPoint(-10.0, 0.0).Touches(BigBox()).Should().BeTrue();
    }

    [Fact]
    public void Touches_PointInsidePolygon_ReturnsFalse()
    {
        new GeoPoint(0.0, 0.0).Touches(BigBox()).Should().BeFalse();
    }

    [Fact]
    public void Within_SmallBoxInsideBigBox_ReturnsTrue()
    {
        SmallBox().Within(BigBox()).Should().BeTrue();
    }

    [Fact]
    public void Within_BigBoxNotWithinSmallBox_ReturnsFalse()
    {
        BigBox().Within(SmallBox()).Should().BeFalse();
    }

    [Fact]
    public void CoveredBy_PointInsidePolygon_ReturnsTrue()
    {
        new GeoPoint(0.0, 0.0).CoveredBy(BigBox()).Should().BeTrue();
    }

    [Fact]
    public void CoveredBy_PointOutsidePolygon_ReturnsFalse()
    {
        new GeoPoint(20.0, 20.0).CoveredBy(BigBox()).Should().BeFalse();
    }

    [Fact]
    public void Covers_BigBoxCoversSmallBox_ReturnsTrue()
    {
        BigBox().Covers(SmallBox()).Should().BeTrue();
    }

    [Fact]
    public void Covers_SmallBoxDoesNotCoverBigBox_ReturnsFalse()
    {
        SmallBox().Covers(BigBox()).Should().BeFalse();
    }

    [Fact]
    public void Disjoint_NonOverlappingGeometries_ReturnsTrue()
    {
        BigBox().Disjoint(new GeoPoint(20.0, 20.0)).Should().BeTrue();
    }

    [Fact]
    public void Disjoint_OverlappingGeometries_ReturnsFalse()
    {
        BigBox().Disjoint(SmallBox()).Should().BeFalse();
    }

    // ── Measurements ──────────────────────────────────────────────────────────

    [Fact]
    public void Distance_SamePoint_IsZero()
    {
        var p = new GeoPoint(1.0, 2.0);
        p.Distance(p).Should().Be(0.0);
    }

    [Fact]
    public void Distance_ThreeFourFiveTriangle_IsFive()
    {
        new GeoPoint(0.0, 0.0).Distance(new GeoPoint(3.0, 4.0))
            .Should().BeApproximately(5.0, 1e-9);
    }

    [Fact]
    public void Area_Point_IsZero()
    {
        new GeoPoint(0.0, 0.0).Area.Should().Be(0.0);
    }

    [Fact]
    public void Area_UnitSquare_IsOne()
    {
        new GeoPolygon([(0.0, 0.0), (1.0, 0.0), (1.0, 1.0), (0.0, 1.0), (0.0, 0.0)])
            .Area.Should().BeApproximately(1.0, 1e-9);
    }

    [Fact]
    public void Length_Point_IsZero()
    {
        new GeoPoint(0.0, 0.0).Length.Should().Be(0.0);
    }

    [Fact]
    public void Length_HorizontalUnitLine_IsOne()
    {
        new GeoLineString([(0.0, 0.0), (1.0, 0.0)])
            .Length.Should().BeApproximately(1.0, 1e-9);
    }

    // ── IsValid / IsEmpty ─────────────────────────────────────────────────────

    [Fact]
    public void IsValid_ValidPolygon_ReturnsTrue()
    {
        BigBox().IsValid.Should().BeTrue();
    }

    [Fact]
    public void IsEmpty_NonEmptyPoint_ReturnsFalse()
    {
        new GeoPoint(0.0, 0.0).IsEmpty.Should().BeFalse();
    }

    [Fact]
    public void IsEmpty_EmptyLineString_ReturnsTrue()
    {
        new GeoLineString().IsEmpty.Should().BeTrue();
    }

    // ── Centroid ──────────────────────────────────────────────────────────────

    [Fact]
    public void Centroid_ReturnsGeoPoint()
    {
        BigBox().Centroid.Should().BeOfType<GeoPoint>();
    }

    [Fact]
    public void Centroid_SymmetricSquare_ReturnsCenter()
    {
        var square = new GeoPolygon([(0.0, 0.0), (2.0, 0.0), (2.0, 2.0), (0.0, 2.0), (0.0, 0.0)]);
        square.Centroid.Longitude.Should().BeApproximately(1.0, 1e-9);
        square.Centroid.Latitude.Should().BeApproximately(1.0, 1e-9);
    }

    // ── Derived geometries ────────────────────────────────────────────────────

    [Fact]
    public void Buffer_Point_ReturnsNonNullGeometry()
    {
        new GeoPoint(0.0, 0.0).Buffer(1.0).Should().NotBeNull();
    }

    [Fact]
    public void Buffer_ZeroDistance_ReturnsGeometry()
    {
        BigBox().Buffer(0.0).Should().NotBeNull();
    }

    [Fact]
    public void ConvexHull_Polygon_ReturnsNonNullGeometry()
    {
        BigBox().ConvexHull().Should().NotBeNull();
    }

    [Fact]
    public void Intersection_OverlappingPolygons_ReturnsCorrectArea()
    {
        var box1 = new GeoPolygon([(-2.0, -2.0), (2.0, -2.0), (2.0, 2.0), (-2.0, 2.0), (-2.0, -2.0)]);
        var box2 = new GeoPolygon([(0.0, -2.0), (4.0, -2.0), (4.0, 2.0), (0.0, 2.0), (0.0, -2.0)]);
        box1.Intersection(box2).Area.Should().BeApproximately(8.0, 1e-9);
    }

    [Fact]
    public void Union_TwoDisjointUnitSquares_ReturnsCombinedArea()
    {
        var sq1 = new GeoPolygon([(0.0, 0.0), (1.0, 0.0), (1.0, 1.0), (0.0, 1.0), (0.0, 0.0)]);
        var sq2 = new GeoPolygon([(2.0, 0.0), (3.0, 0.0), (3.0, 1.0), (2.0, 1.0), (2.0, 0.0)]);
        sq1.Union(sq2).Area.Should().BeApproximately(2.0, 1e-9);
    }

    [Fact]
    public void Difference_BigMinusSmall_ReturnsReducedArea()
    {
        var big = new GeoPolygon([(-2.0, -2.0), (2.0, -2.0), (2.0, 2.0), (-2.0, 2.0), (-2.0, -2.0)]);
        var small = new GeoPolygon([(0.0, 0.0), (1.0, 0.0), (1.0, 1.0), (0.0, 1.0), (0.0, 0.0)]);
        big.Difference(small).Area.Should().BeApproximately(15.0, 1e-9);
    }

    [Fact]
    public void SymmetricDifference_PartialOverlap_ReturnsNonOverlappingArea()
    {
        var box1 = new GeoPolygon([(-1.0, -1.0), (1.0, -1.0), (1.0, 1.0), (-1.0, 1.0), (-1.0, -1.0)]);
        var box2 = new GeoPolygon([(0.0, -1.0), (2.0, -1.0), (2.0, 1.0), (0.0, 1.0), (0.0, -1.0)]);
        box1.SymmetricDifference(box2).Area.Should().BeApproximately(4.0, 1e-9);
    }

    // ── FromNts ───────────────────────────────────────────────────────────────

    [Fact]
    public void FromNts_NtsPoint_ReturnsGeoPoint()
    {
        GeoGeometry.FromNts(new GeoPoint(1.0, 2.0).NtsGeometry)
            .Should().BeOfType<GeoPoint>();
    }

    [Fact]
    public void FromNts_NtsLineString_ReturnsGeoLineString()
    {
        GeoGeometry.FromNts(new GeoLineString([(0.0, 0.0), (1.0, 1.0)]).NtsGeometry)
            .Should().BeOfType<GeoLineString>();
    }

    [Fact]
    public void FromNts_NtsPolygon_ReturnsGeoPolygon()
    {
        GeoGeometry.FromNts(BigBox().NtsGeometry).Should().BeOfType<GeoPolygon>();
    }

    [Fact]
    public void FromNts_NtsMultiPoint_ReturnsGeoRawGeometry()
    {
        var factory = NetTopologySuite.NtsGeometryServices.Instance.CreateGeometryFactory(srid: 4326);
        var multiPoint = factory.CreateMultiPoint(
            [factory.CreatePoint(new Coordinate(0.0, 0.0))]);
        GeoGeometry.FromNts(multiPoint).Should().BeOfType<GeoRawGeometry>();
    }
}
