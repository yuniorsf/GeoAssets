using FluentAssertions;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Shared.Services.Evidence;
using GeoAssets.Shared.Services.FieldWork;
using Xunit;

namespace GeoAssets.Shared.Tests.Services.FieldWork;

public class PoleCensusProximityGateTests
{
    private static readonly GeoPoint PoleLocation = new(-46.633308, -23.550520);

    [Fact]
    public void IsAcceptable_WithinRangeAndAccurate_ReturnsTrue()
    {
        var fix = new GpsFix(-23.550520, -46.633308, HorizontalAccuracyMeters: 5.0, IsFromMockProvider: false);

        PoleCensusProximityGate.IsAcceptable(fix, PoleLocation).Should().BeTrue();
    }

    [Fact]
    public void IsAcceptable_FarFromPole_ReturnsFalse()
    {
        // Roughly 1km north — well outside the 15m tolerance.
        var fix = new GpsFix(-23.541, -46.633308, HorizontalAccuracyMeters: 5.0, IsFromMockProvider: false);

        PoleCensusProximityGate.IsAcceptable(fix, PoleLocation).Should().BeFalse();
    }

    [Fact]
    public void IsAcceptable_InaccurateFixEvenWhenClose_ReturnsFalse()
    {
        var fix = new GpsFix(-23.550520, -46.633308, HorizontalAccuracyMeters: 50.0, IsFromMockProvider: false);

        PoleCensusProximityGate.IsAcceptable(fix, PoleLocation).Should().BeFalse();
    }

    [Fact]
    public void IsAcceptable_MockProviderEvenWhenCloseAndAccurate_ReturnsFalse()
    {
        var fix = new GpsFix(-23.550520, -46.633308, HorizontalAccuracyMeters: 1.0, IsFromMockProvider: true);

        PoleCensusProximityGate.IsAcceptable(fix, PoleLocation).Should().BeFalse();
    }

    [Fact]
    public void IsAcceptable_NullFix_Throws()
    {
        var act = () => PoleCensusProximityGate.IsAcceptable(null!, PoleLocation);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void IsAcceptable_NullPoleLocation_Throws()
    {
        var fix = new GpsFix(-23.550520, -46.633308, HorizontalAccuracyMeters: 5.0, IsFromMockProvider: false);
        var act = () => PoleCensusProximityGate.IsAcceptable(fix, null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
