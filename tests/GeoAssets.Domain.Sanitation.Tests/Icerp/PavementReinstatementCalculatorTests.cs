using FluentAssertions;
using GeoAssets.Domain.Sanitation.Icerp;
using Xunit;

namespace GeoAssets.Domain.Sanitation.Tests.Icerp;

public class PavementReinstatementCalculatorTests
{
    [Fact]
    public void CalculateReinstatementArea_AddsMarginToEveryEdge()
    {
        var trench = new TrenchDimensions(LengthMeters: 4.0, WidthMeters: 0.6);

        var area = PavementReinstatementCalculator.CalculateReinstatementAreaSquareMeters(trench, marginMeters: 0.2);

        // (4 + 0.4) * (0.6 + 0.4) = 4.4 * 1.0
        area.Should().BeApproximately(4.4, 1e-9);
    }

    [Fact]
    public void CalculateReinstatementArea_ZeroMargin_EqualsRawTrenchArea()
    {
        var trench = new TrenchDimensions(LengthMeters: 3.0, WidthMeters: 1.0);

        var area = PavementReinstatementCalculator.CalculateReinstatementAreaSquareMeters(trench, marginMeters: 0);

        area.Should().BeApproximately(trench.AreaSquareMeters, 1e-9);
    }

    [Fact]
    public void CalculateReinstatementArea_NegativeMargin_Throws()
    {
        var trench = new TrenchDimensions(1, 1);

        var act = () => PavementReinstatementCalculator.CalculateReinstatementAreaSquareMeters(trench, marginMeters: -0.1);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void TrenchDimensions_AreaSquareMeters_IsLengthTimesWidth()
    {
        var trench = new TrenchDimensions(LengthMeters: 5.0, WidthMeters: 0.8);

        trench.AreaSquareMeters.Should().BeApproximately(4.0, 1e-9);
    }
}
