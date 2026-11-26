using FluentAssertions;
using GeoAssets.Ingestion.Proj;
using GeoAssets.Ingestion.Spatial;
using GeoAssets.Ingestion.Tests.Fakes;
using NetTopologySuite.Geometries;
using Xunit;

namespace GeoAssets.Ingestion.Tests.Proj;

public sealed class ProjCoordinateTransformerFactoryTests
{
    private const string ProjDataDirectory = "/opt/homebrew/share/proj";
    private const string GridFile = "br_ibge_SAD69_003.tif";
    private const double SourceX = 554271.85773680;
    private const double SourceY = 8566172.49068071;
    private const string Sirgas2000GeographicWkt =
        "GEOGCS[\"SIRGAS 2000\",DATUM[\"Sirgas2000\",SPHEROID[\"GRS 1980\",6378137,298.257222101]],PRIMEM[\"Greenwich\",0],UNIT[\"degree\",0.0174532925199433]]";

    private static readonly GeometryFactory Factory = new();

    private static bool GridIsAvailable() => File.Exists(Path.Combine(ProjDataDirectory, GridFile));

    private static ICoordinateTransformer Create(string wkt, string? projData = null) =>
        new ProjCoordinateTransformerFactory(new ProjOptions(projData)).Create(wkt);

    private static (double X, double Y) TransformPoint(ICoordinateTransformer transformer, double x, double y)
    {
        var result = (Point)transformer.Transform(Factory.CreatePoint(new Coordinate(x, y)));
        return (result.X, result.Y);
    }

    private static double MetersBetween((double X, double Y) a, (double X, double Y) b)
    {
        const double metersPerDegree = 111_320.0;
        var dx = (a.X - b.X) * metersPerDegree * Math.Cos(a.Y * Math.PI / 180.0);
        var dy = (a.Y - b.Y) * metersPerDegree;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    [Fact]
    public void Transform_Sad69WithTheGridAvailable_IsWithinTenCentimetresOfTheVectorOrHelmertWithout()
    {
        var transformer = Create(ShapefileDeliveryBuilder.Sad69Utm24SWkt, ProjDataDirectory);

        var result = TransformPoint(transformer, SourceX, SourceY);

        if (GridIsAvailable())
        {
            transformer.Accuracy.Should().Be("grid");
            MetersBetween(result, (-38.5, -12.97)).Should().BeLessThan(0.1);
        }
        else
        {
            transformer.Accuracy.Should().Be("helmert");
            result.X.Should().BeApproximately(-38.49995499, 1e-6);
            result.Y.Should().BeApproximately(-12.97000519, 1e-6);
            MetersBetween(result, (-38.5, -12.97)).Should().BeInRange(4.0, 6.0);
        }
    }

    [Fact]
    public void Transform_CoelbaPrj_HonoursItsMinus42CentralMeridianDespiteTheUtm24SLabel()
    {
        const string coelbaPrj =
            "PROJCS[\"SAD_1969_UTM_Zone_24S\",GEOGCS[\"GCS_South_American_1969\",DATUM[\"D_South_American_1969\",SPHEROID[\"GRS_1967_Truncated\",6378160.0,298.25]],PRIMEM[\"Greenwich\",0.0],UNIT[\"Degree\",0.0174532925199433]],PROJECTION[\"Transverse_Mercator\"],PARAMETER[\"False_Easting\",500000.0],PARAMETER[\"False_Northing\",10000000.0],PARAMETER[\"Central_Meridian\",-42.0],PARAMETER[\"Scale_Factor\",0.9996],PARAMETER[\"Latitude_Of_Origin\",0.0],UNIT[\"Meter\",1.0]]";
        var transformer = Create(coelbaPrj, ProjDataDirectory);

        var result = TransformPoint(transformer, 879850.06698507, 8563619.15135202);

        MetersBetween(result, (-38.5, -12.97)).Should().BeLessThan(GridIsAvailable() ? 0.1 : 6.0);
    }

    [Fact]
    public void Transform_Sad69WithoutTheGridDirectory_UsesTheHelmertShift()
    {
        var transformer = Create(ShapefileDeliveryBuilder.Sad69Utm24SWkt);

        var result = TransformPoint(transformer, SourceX, SourceY);

        transformer.Accuracy.Should().Be("helmert");
        result.X.Should().BeApproximately(-38.49995499, 1e-6);
        result.Y.Should().BeApproximately(-12.97000519, 1e-6);
        MetersBetween(result, (-38.5, -12.97)).Should().BeInRange(4.0, 6.0);
    }

    [Fact]
    public void Transform_Sad69_IsNeverTheSixtyFiveMetreUnshiftedResult()
    {
        var transformer = Create(ShapefileDeliveryBuilder.Sad69Utm24SWkt);

        var result = TransformPoint(transformer, SourceX, SourceY);

        MetersBetween(result, (-38.5, -12.97)).Should().BeLessThan(10.0);
    }

    [Fact]
    public void Transform_OutputIsLongitudeThenLatitude()
    {
        var result = TransformPoint(Create(ShapefileDeliveryBuilder.Sad69Utm24SWkt), SourceX, SourceY);

        result.X.Should().BeInRange(-39.0, -38.0);
        result.Y.Should().BeInRange(-13.5, -12.5);
    }

    [Fact]
    public void Transform_OutputGeometryCarriesSrid4326()
    {
        var transformer = Create(ShapefileDeliveryBuilder.Sad69Utm24SWkt);

        var result = transformer.Transform(Factory.CreatePoint(new Coordinate(SourceX, SourceY)));

        result.SRID.Should().Be(4326);
    }

    [Fact]
    public void Transform_LineStringKeepsEveryVertexInOrder()
    {
        var transformer = Create(ShapefileDeliveryBuilder.Sad69Utm24SWkt);
        var line = Factory.CreateLineString([new Coordinate(SourceX, SourceY), new Coordinate(SourceX + 100, SourceY + 100)]);

        var result = (LineString)transformer.Transform(line);

        result.NumPoints.Should().Be(2);
        result.StartPoint.X.Should().BeLessThan(result.EndPoint.X);
        result.StartPoint.Y.Should().BeLessThan(result.EndPoint.Y);
    }

    [Fact]
    public void Transform_DoesNotMutateTheInputGeometry()
    {
        var transformer = Create(ShapefileDeliveryBuilder.Sad69Utm24SWkt);
        var point = Factory.CreatePoint(new Coordinate(SourceX, SourceY));

        transformer.Transform(point);

        point.X.Should().Be(SourceX);
        point.Y.Should().Be(SourceY);
    }

    [Fact]
    public void Create_GeographicSirgas2000Source_NeedsNoDatumShift()
    {
        var transformer = Create(Sirgas2000GeographicWkt);

        var result = TransformPoint(transformer, -38.5, -12.97);

        transformer.Accuracy.Should().Be("none");
        result.X.Should().BeApproximately(-38.5, 1e-7);
        result.Y.Should().BeApproximately(-12.97, 1e-7);
    }

    [Fact]
    public void Create_GridDirectory_WhenPresent_ReportsGridAccuracy()
    {
        var transformer = Create(ShapefileDeliveryBuilder.Sad69Utm24SWkt, ProjDataDirectory);

        transformer.Accuracy.Should().Be(GridIsAvailable() ? "grid" : "helmert");
    }

    [Theory]
    [InlineData("not a wkt")]
    [InlineData("PROJCS[\"broken\"")]
    public void Create_InvalidWkt_ThrowsNotSupported(string wkt)
    {
        var act = () => Create(wkt);

        act.Should().Throw<NotSupportedException>();
    }
}
