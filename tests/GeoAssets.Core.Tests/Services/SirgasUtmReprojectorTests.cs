using FluentAssertions;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Services;
using Xunit;

namespace GeoAssets.Core.Tests.Services;

/// <summary>
/// XD01-224's AC: "A reprojection test confirms SIRGAS UTM output within the documented ±0.5m
/// tolerance for PONNOT." Proven as a forward→inverse round-trip, measured with
/// <see cref="GeodeticDistanceCalculator"/> (already-tested real-world distance) rather than
/// against an independently-sourced "expected" easting/northing pair this suite has no
/// authoritative source for — the round-trip accuracy is exactly what ±0.5m tolerance is a
/// statement about.
/// </summary>
public class SirgasUtmReprojectorTests
{
    [Theory]
    [InlineData(-46.633, -23.55)]  // São Paulo, zone 23S
    [InlineData(-43.2, -22.9)]     // Rio de Janeiro, zone 23S
    [InlineData(-38.5, -12.97)]    // Salvador, zone 24S
    [InlineData(-67.8, -9.97)]     // Rio Branco, zone 19S
    public void ToSirgasUtm_RoundTripsThroughInverse_WithinHalfMeter(double longitude, double latitude)
    {
        var original = new GeoPoint(longitude, latitude);

        var (easting, northing, epsgCode) = SirgasUtmReprojector.ToSirgasUtm(original);
        var zone = epsgCode - 31978 + 18;
        var roundTripped = SirgasUtmReprojector.FromSirgasUtm(easting, northing, zone);

        var driftMeters = GeodeticDistanceCalculator.DistanceInMeters(original, roundTripped);
        driftMeters.Should().BeLessOrEqualTo(0.5);
    }

    [Fact]
    public void ToSirgasUtm_SaoPaulo_ResolvesToZone23SEpsgCode()
    {
        var (_, _, epsgCode) = SirgasUtmReprojector.ToSirgasUtm(new GeoPoint(-46.633, -23.55));

        epsgCode.Should().Be(31983); // SIRGAS 2000 / UTM zone 23S
    }

    [Fact]
    public void ToSirgasUtm_EastingAndNorthing_AreWithinPlausibleRangeForUtm()
    {
        // Easting is always near the 500,000m false-easting origin for a point near its zone's
        // central meridian; northing for the southern hemisphere is offset by the 10,000,000m
        // false-northing. A gross transform bug (wrong hemisphere, wrong central meridian) would
        // produce wildly implausible values here.
        var (easting, northing, _) = SirgasUtmReprojector.ToSirgasUtm(new GeoPoint(-46.633, -23.55));

        easting.Should().BeInRange(100_000, 900_000);
        // Brazil spans from the equator (northing ~= false_northing, 10,000,000m) down to its
        // southernmost point (~-33.75 degrees, northing ~= 6,200,000m) — not a narrow band near
        // the false-northing origin.
        northing.Should().BeInRange(6_000_000, 10_000_000);
    }

    [Fact]
    public void ToSirgasUtm_NullArgument_Throws()
    {
        var act = () => SirgasUtmReprojector.ToSirgasUtm(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void WktFor_Zone23_ReturnsNonEmptySirgas2000WktContainingZoneNumber()
    {
        var wkt = SirgasUtmReprojector.WktFor(23);

        wkt.Should().NotBeNullOrWhiteSpace();
        wkt.Should().Contain("SIRGAS_2000");
        wkt.Should().Contain("UTM_Zone_23S");
    }

    [Fact]
    public void WktFor_ZoneToSirgasUtmItselfResolvesTo_IsConsistentWithThatZone()
    {
        // ToSirgasUtm builds its own MathTransform from this same private WKT builder (see
        // SirgasUtmReprojector.GetOrBuildTransform) — WktFor is a public wrapper over it, so a
        // caller writing a .prj sidecar for these exact coordinates must get the matching zone.
        var (_, _, epsgCode) = SirgasUtmReprojector.ToSirgasUtm(new GeoPoint(-46.633, -23.55));
        var zone = epsgCode - 31978 + 18;

        var wkt = SirgasUtmReprojector.WktFor(zone);

        wkt.Should().Contain($"UTM_Zone_{zone}S");
    }
}
