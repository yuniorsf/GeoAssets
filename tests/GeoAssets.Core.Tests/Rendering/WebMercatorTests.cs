using FluentAssertions;
using GeoAssets.Core.Rendering;
using Xunit;

namespace GeoAssets.Core.Tests.Rendering;

/// <summary>XD01-431 — <see cref="WebMercator"/> (EPSG:3857 ↔ 4326).</summary>
public class WebMercatorTests
{
    [Fact]
    public void Constants_AreTheEpsg3857Ones()
    {
        WebMercator.HalfWorldSize.Should().BeApproximately(20037508.342789244, 1e-6);
        WebMercator.MaxLatitude.Should().BeApproximately(85.0511287798066, 1e-12);
    }

    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(180, 0, 20037508.342789244, 0)]
    [InlineData(-180, 0, -20037508.342789244, 0)]
    [InlineData(-38.5014, -12.9714, -4285956.242828143, -1456464.9771951567)]
    [InlineData(2.2945, 48.8584, 255422.57162516622, 6250868.901479604)]
    public void Project_IsTheSphericalMercator(double lon, double lat, double x, double y)
    {
        var (px, py) = WebMercator.Project(lon, lat);

        px.Should().BeApproximately(x, 1e-3);
        py.Should().BeApproximately(y, 1e-3);
    }

    [Theory]
    [InlineData(90)]
    [InlineData(89)]
    [InlineData(1e9)]
    public void Project_ClampsThePolesToTheWorldsEdge(double lat)
    {
        WebMercator.Project(0, lat).Y.Should().BeApproximately(WebMercator.HalfWorldSize, 1e-6);
        WebMercator.Project(0, -lat).Y.Should().BeApproximately(-WebMercator.HalfWorldSize, 1e-6);
    }

    [Fact]
    public void Project_ClampsLongitudesBeyondTheAntimeridian()
    {
        WebMercator.Project(190, 0).X.Should().Be(WebMercator.HalfWorldSize);
        WebMercator.Project(-190, 0).X.Should().Be(-WebMercator.HalfWorldSize);
    }

    [Theory]
    [InlineData(-38.5014, -12.9714)]
    [InlineData(139.6917, 35.6895)]
    [InlineData(0, 85)]
    [InlineData(-179.999, -84.9)]
    public void Unproject_InvertsProject(double lon, double lat)
    {
        var (x, y) = WebMercator.Project(lon, lat);

        var (lon2, lat2) = WebMercator.Unproject(x, y);

        lon2.Should().BeApproximately(lon, 1e-9);
        lat2.Should().BeApproximately(lat, 1e-9);
    }

    [Fact]
    public void Unproject_ClampsToTheWorld()
    {
        var (lon, lat) = WebMercator.Unproject(1e12, -1e12);

        lon.Should().Be(180);
        lat.Should().BeApproximately(-WebMercator.MaxLatitude, 1e-12);
    }

    [Fact]
    public void Bounds_RoundTrip()
    {
        var box = new BoundingBox(-39, -14, -38, -12);

        var back = WebMercator.Unproject(WebMercator.Project(box));

        back.MinLon.Should().BeApproximately(-39, 1e-9);
        back.MinLat.Should().BeApproximately(-14, 1e-9);
        back.MaxLon.Should().BeApproximately(-38, 1e-9);
        back.MaxLat.Should().BeApproximately(-12, 1e-9);
    }

    [Theory]
    [InlineData(double.NaN, 0)]
    [InlineData(0, double.PositiveInfinity)]
    public void Project_And_Unproject_RefuseNonFiniteCoordinates(double a, double b)
    {
        var project = () => WebMercator.Project(a, b);
        var unproject = () => WebMercator.Unproject(a, b);

        project.Should().Throw<ArgumentException>();
        unproject.Should().Throw<ArgumentException>();
    }
}
