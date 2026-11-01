using FluentAssertions;
using GeoAssets.Domain.Electrical.Continuity;
using Xunit;

namespace GeoAssets.Domain.Electrical.Tests.Continuity;

public class CompensationCalculatorTests
{
    private static readonly CompensationCoefficients Coefficients = new(Dic: 10m, Fic: 5m, Dmic: 3m);

    [Fact]
    public void Calculate_NoIndicatorExceeded_ResultIsZero()
    {
        var compliance = new ContinuityComplianceResult(
            new IndicatorComplianceStatus(false, 0), new IndicatorComplianceStatus(false, 0), new IndicatorComplianceStatus(false, 0));

        var result = CompensationCalculator.Calculate(compliance, Coefficients, referenceValue: 100m);

        result.Total.Should().Be(0m);
    }

    [Fact]
    public void Calculate_DicExceededByHalf_AppliesCoefficientTimesRatioTimesReferenceValue()
    {
        var compliance = new ContinuityComplianceResult(
            new IndicatorComplianceStatus(true, 0.5), new IndicatorComplianceStatus(false, 0), new IndicatorComplianceStatus(false, 0));

        var result = CompensationCalculator.Calculate(compliance, Coefficients, referenceValue: 100m);

        result.Dic.Should().Be(10m * 0.5m * 100m);
        result.Fic.Should().Be(0m);
        result.Dmic.Should().Be(0m);
        result.Total.Should().Be(result.Dic);
    }

    [Fact]
    public void Calculate_MultipleIndicatorsExceeded_TotalIsTheSumOfEach()
    {
        var compliance = new ContinuityComplianceResult(
            new IndicatorComplianceStatus(true, 0.2), new IndicatorComplianceStatus(true, 1.0), new IndicatorComplianceStatus(false, 0));

        var result = CompensationCalculator.Calculate(compliance, Coefficients, referenceValue: 50m);

        var expectedDic = 10m * 0.2m * 50m;
        var expectedFic = 5m * 1.0m * 50m;
        result.Total.Should().Be(expectedDic + expectedFic);
    }
}
