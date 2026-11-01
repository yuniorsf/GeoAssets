using FluentAssertions;
using GeoAssets.Domain.Sanitation.Icerp;
using Xunit;

namespace GeoAssets.Domain.Sanitation.Tests.Icerp;

public class CbuqReinstatementCalculatorTests
{
    [Fact]
    public void CalculateVolume_IsAreaTimesThickness()
    {
        var volume = CbuqReinstatementCalculator.CalculateVolumeCubicMeters(areaSquareMeters: 4.4, thicknessMeters: 0.05);

        volume.Should().BeApproximately(0.22, 1e-9);
    }

    [Fact]
    public void CalculateVolume_NegativeArea_Throws()
    {
        var act = () => CbuqReinstatementCalculator.CalculateVolumeCubicMeters(-1, 0.05);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void CalculateVolume_NegativeThickness_Throws()
    {
        var act = () => CbuqReinstatementCalculator.CalculateVolumeCubicMeters(4.4, -0.05);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void CalculateVolume_ZeroArea_IsZero()
    {
        CbuqReinstatementCalculator.CalculateVolumeCubicMeters(0, 0.05).Should().Be(0);
    }
}
