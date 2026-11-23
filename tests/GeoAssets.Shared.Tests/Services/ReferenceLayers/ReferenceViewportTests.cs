using FluentAssertions;
using GeoAssets.Core.Rendering;
using GeoAssets.Shared.Services.ReferenceLayers;
using Xunit;

namespace GeoAssets.Shared.Tests.Services.ReferenceLayers;

/// <summary>
/// XD01-331 — <see cref="ReferenceViewport.Normalize"/> turns whatever Leaflet reports into a <c>bbox</c> the
/// reference-features route accepts (XD01-330 answers <c>400</c> outside ±180/±90, for non-finite values or min &gt; max).
/// </summary>
public sealed class ReferenceViewportTests
{
    [Fact]
    public void AnOrdinaryView_IsAskedForAsItIs() =>
        ReferenceViewport.Normalize(-38.6, -13.0, -38.4, -12.8).Should().Be(new BoundingBox(-38.6, -13.0, -38.4, -12.8));

    [Fact]
    public void LatitudesBeyondThePoles_AreClamped() =>
        ReferenceViewport.Normalize(-10, -95, 10, 91).Should().Be(new BoundingBox(-10, -90, 10, 90));

    [Theory]
    [InlineData(-200, 200)]
    [InlineData(-180, 180)]
    [InlineData(0, 360)]
    public void AViewAtLeastAFullTurnWide_AsksForEveryLongitude(double minLon, double maxLon) =>
        ReferenceViewport.Normalize(minLon, -10, maxLon, 10).Should().Be(new BoundingBox(-180, -10, 180, 10));

    [Theory]
    [InlineData(321.4, 321.6, -38.6, -38.4)]   // one wrapped copy east
    [InlineData(-398.6, -398.4, -38.6, -38.4)] // one wrapped copy west
    [InlineData(180, 181, -180, -179)]         // the antimeridian itself, from the east side
    public void AViewOnAWrappedCopyOfTheWorld_AsksForTheLongitudesItShows(double minLon, double maxLon, double west, double east)
    {
        var bbox = ReferenceViewport.Normalize(minLon, -13, maxLon, -12)!.Value;

        bbox.MinLon.Should().BeApproximately(west, 1e-9);
        bbox.MaxLon.Should().BeApproximately(east, 1e-9);
        bbox.MinLat.Should().Be(-13);
        bbox.MaxLat.Should().Be(-12);
    }

    [Theory]
    [InlineData(170, 190)]
    [InlineData(-190, -170)]
    [InlineData(530, 550)]
    public void AViewAcrossTheAntimeridian_AsksForEveryLongitude(double minLon, double maxLon) =>
        ReferenceViewport.Normalize(minLon, -10, maxLon, 10).Should().Be(new BoundingBox(-180, -10, 180, 10));

    [Theory]
    [InlineData(double.NaN, 0, 1, 1)]
    [InlineData(0, double.NegativeInfinity, 1, 1)]
    [InlineData(0, 0, double.PositiveInfinity, 1)]
    [InlineData(0, 0, 1, double.NaN)]
    [InlineData(2, 0, 1, 1)]
    [InlineData(0, 2, 1, 1)]
    public void NonFiniteOrInvertedBounds_AskForNothing(double minLon, double minLat, double maxLon, double maxLat) =>
        ReferenceViewport.Normalize(minLon, minLat, maxLon, maxLat).Should().BeNull();

    [Fact]
    public void EveryNormalizedBox_IsOneTheServerAccepts()
    {
        double[] longitudes = [-725, -540, -361, -180, -179.9, -1, 0, 0.5, 179.9, 180, 181, 359, 540, 721];
        foreach (var west in longitudes)
        foreach (var width in new[] { 0.0, 0.01, 10, 179, 359.9, 360, 800 })
        {
            var bbox = ReferenceViewport.Normalize(west, -100, west + width, 100);

            bbox.Should().NotBeNull();
            var b = bbox!.Value;
            b.MinLon.Should().BeInRange(-180, 180);
            b.MaxLon.Should().BeInRange(-180, 180);
            b.MinLon.Should().BeLessThanOrEqualTo(b.MaxLon);
            b.MinLat.Should().Be(-90);
            b.MaxLat.Should().Be(90);
        }
    }
}
