using FluentAssertions;
using GeoAssets.Core.Services;
using Xunit;

namespace GeoAssets.Core.Tests.Services;

public class SirgasUtmZoneResolverTests
{
    [Theory]
    [InlineData(-75.0, 18)]
    [InlineData(-69.0, 19)]
    [InlineData(-63.0, 20)]
    [InlineData(-57.0, 21)]
    [InlineData(-51.0, 22)]
    [InlineData(-46.633, 23)] // São Paulo
    [InlineData(-39.0, 24)]
    [InlineData(-33.0, 25)]
    public void ZoneFor_WithinBrazilsLongitudeRange_ReturnsExpectedZone(double longitude, int expectedZone) =>
        SirgasUtmZoneResolver.ZoneFor(longitude).Should().Be(expectedZone);

    [Theory]
    [InlineData(18, 31978)]
    [InlineData(19, 31979)]
    [InlineData(25, 31985)]
    public void EpsgCodeFor_MatchesDocumentedRange(int zone, int expectedEpsgCode) =>
        SirgasUtmZoneResolver.EpsgCodeFor(SirgasUtmZoneResolver.CentralMeridianFor(zone)).Should().Be(expectedEpsgCode);

    [Fact]
    public void ZoneFor_OutsideDocumentedRange_Throws()
    {
        var act = () => SirgasUtmZoneResolver.ZoneFor(-170.0); // nowhere near Brazil

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void CentralMeridianFor_Zone23_IsMinusFortyFive()
    {
        // Zone 23S covers São Paulo/Rio de Janeiro — a known, checkable reference point.
        SirgasUtmZoneResolver.CentralMeridianFor(23).Should().Be(-45.0);
    }
}
