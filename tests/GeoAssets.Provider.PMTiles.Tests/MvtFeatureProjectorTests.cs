using FluentAssertions;
using GeoAssets.Core.Models;
using GeoAssets.Core.Rendering;
using GeoAssets.Provider.PMTiles.Mvt;
using Xunit;

namespace GeoAssets.Provider.PMTiles.Tests;

/// <summary>XD01-438 — <see cref="MvtFeatureProjector"/>: tile coordinates to longitude/latitude, geometry kinds and properties.</summary>
public sealed class MvtFeatureProjectorTests
{
    private static readonly TileAddress World = new(0, 0, 0);

    private static MvtLayer Layer(params MvtFeature[] features) => new("roads", 4096, features);

    private static MvtFeature Feature(MvtGeometryType type, params MvtPoint[][] parts) =>
        new(null, type, new Dictionary<string, string>(StringComparer.Ordinal), parts);

    [Fact]
    public void TheTileCenter_IsTheOriginOfTheWorld_AndItsCornerTheWorldsCorner()
    {
        var feature = Feature(MvtGeometryType.Point, [new MvtPoint(2048, 2048)], [new MvtPoint(0, 0)]);

        var projected = MvtFeatureProjector.ToGeoFeature(feature, Layer(feature), World, "f")!;

        var coordinates = projected.Geometry!.NtsGeometry.Coordinates;
        coordinates[0].X.Should().BeApproximately(0, 1e-7);
        coordinates[0].Y.Should().BeApproximately(0, 1e-7);
        coordinates[1].X.Should().BeApproximately(-180, 1e-7);
        coordinates[1].Y.Should().BeApproximately(WebMercator.MaxLatitude, 1e-6);
        projected.Geometry.Type.Should().Be("MultiPoint");
    }

    [Fact]
    public void ATilesCoordinates_FallInsideItsEnvelope()
    {
        var tile = new TileAddress(10, 301, 384);
        var feature = Feature(MvtGeometryType.Point, [new MvtPoint(1024, 3072)]);

        var point = MvtFeatureProjector.ToGeoFeature(feature, Layer(feature), tile, "f")!.Geometry!.NtsGeometry.Coordinate;

        var envelope = tile.Envelope4326;
        point.X.Should().BeInRange(envelope.MinLon, envelope.MaxLon);
        point.Y.Should().BeInRange(envelope.MinLat, envelope.MaxLat);
        point.X.Should().BeApproximately(envelope.MinLon + ((envelope.MaxLon - envelope.MinLon) / 4), 1e-6);
    }

    [Fact]
    public void APointInTheTilesBuffer_IsLeftToTheNeighbouringTile()
    {
        var feature = Feature(MvtGeometryType.Point, [new MvtPoint(-10, 50)], [new MvtPoint(4096, 50)]);

        MvtFeatureProjector.ToGeoFeature(feature, Layer(feature), World, "f").Should().BeNull();
    }

    [Fact]
    public void Lines_AreLineStrings_OrMultiLineStrings_ADegeneratePartDropped()
    {
        var single = Feature(MvtGeometryType.LineString, [new MvtPoint(0, 0), new MvtPoint(10, 10)], [new MvtPoint(5, 5)]);
        var multi = Feature(MvtGeometryType.LineString, [new MvtPoint(0, 0), new MvtPoint(10, 10)], [new MvtPoint(20, 20), new MvtPoint(30, 30)]);

        MvtFeatureProjector.ToGeoFeature(single, Layer(single), World, "a")!.Geometry!.Type.Should().Be("LineString");
        MvtFeatureProjector.ToGeoFeature(multi, Layer(multi), World, "b")!.Geometry!.Type.Should().Be("MultiLineString");
    }

    [Fact]
    public void ARingOfTheFirstRingsWinding_StartsAPolygon_AndOneOfTheOppositeWindingIsItsHole()
    {
        MvtPoint[] shell = [new(0, 0), new(1000, 0), new(1000, 1000), new(0, 1000)];
        MvtPoint[] hole = [new(200, 200), new(200, 400), new(400, 400), new(400, 200)];
        MvtPoint[] secondShell = [new(2000, 2000), new(3000, 2000), new(3000, 3000), new(2000, 3000)];

        var withHole = Feature(MvtGeometryType.Polygon, shell, hole);
        var polygon = MvtFeatureProjector.ToGeoFeature(withHole, Layer(withHole), World, "a")!.Geometry!.NtsGeometry
            .Should().BeOfType<NetTopologySuite.Geometries.Polygon>().Subject;
        polygon.NumInteriorRings.Should().Be(1);
        polygon.ExteriorRing.IsClosed.Should().BeTrue();

        var two = Feature(MvtGeometryType.Polygon, shell, hole, secondShell);
        var multi = MvtFeatureProjector.ToGeoFeature(two, Layer(two), World, "b")!.Geometry!.NtsGeometry
            .Should().BeOfType<NetTopologySuite.Geometries.MultiPolygon>().Subject;
        multi.NumGeometries.Should().Be(2);
    }

    [Fact]
    public void AZeroAreaPolygon_HasNoFeature()
    {
        var flat = Feature(MvtGeometryType.Polygon, [new MvtPoint(0, 0), new MvtPoint(10, 0), new MvtPoint(20, 0)]);

        MvtFeatureProjector.ToGeoFeature(flat, Layer(flat), World, "f").Should().BeNull();
    }

    [Fact]
    public void AnUnknownGeometry_HasNoFeature()
    {
        var unknown = Feature(MvtGeometryType.Unknown, [new MvtPoint(1, 1)]);

        MvtFeatureProjector.ToGeoFeature(unknown, Layer(unknown), World, "f").Should().BeNull();
    }

    [Fact]
    public void Properties_CarryTheLayerTheNameTheTagsAndTheMvtId_TheAssetTypeByGeometry()
    {
        var feature = new MvtFeature(
            7,
            MvtGeometryType.LineString,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["name"] = "Rua Augusta", ["highway"] = "primary", ["mvt:layer"] = "spoof" },
            [[new MvtPoint(0, 0), new MvtPoint(10, 10)]]);

        var projected = MvtFeatureProjector.ToGeoFeature(feature, Layer(feature), World, "pmtiles:x:0/0/0:0:0")!;

        projected.Id.Should().Be("pmtiles:x:0/0/0:0:0");
        projected.Properties.Name.Should().Be("Rua Augusta");
        projected.Properties.AssetTypeId.Should().Be(AssetType.Line.Id.ToString());
        projected.Properties.CustomAttributes.Should().Contain(MvtFeatureProjector.LayerAttribute, "roads", "the layer is never shadowed by a tag")
            .And.Contain("highway", "primary")
            .And.Contain("mvt:id", "7");
    }

    [Theory]
    [InlineData((int)MvtGeometryType.Point, "00000000-0000-0000-0000-000000000001")]
    [InlineData((int)MvtGeometryType.LineString, "00000000-0000-0000-0000-000000000002")]
    [InlineData((int)MvtGeometryType.Polygon, "00000000-0000-0000-0000-000000000003")]
    public void TheAssetType_IsTheDefaultTypeOfTheGeometry(int type, string assetTypeId)
    {
        MvtPoint[] part = [new(10, 10), new(100, 10), new(100, 100)];
        var feature = Feature((MvtGeometryType)type, part);

        MvtFeatureProjector.ToGeoFeature(feature, Layer(feature), World, "f")!.Properties.AssetTypeId.Should().Be(assetTypeId);
    }
}
