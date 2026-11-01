using FluentAssertions;
using GeoAssets.Domain.Electrical.Continuity;
using Xunit;

namespace GeoAssets.Domain.Electrical.Tests.Continuity;

public class ContinuityComplianceEvaluatorTests
{
    private static readonly ContinuityComplianceLimits Limits = new(
        DicLimit: TimeSpan.FromHours(10),
        FicLimit: 5,
        DmicLimit: TimeSpan.FromHours(3));

    [Fact]
    public void Evaluate_AllWithinLimits_NothingExceeded()
    {
        var indicators = new ContinuityIndicators(TimeSpan.FromHours(5), 2, TimeSpan.FromHours(1));

        var result = ContinuityComplianceEvaluator.Evaluate(indicators, Limits);

        result.AnyExceeded.Should().BeFalse();
        result.Dic.Exceeded.Should().BeFalse();
        result.Fic.Exceeded.Should().BeFalse();
        result.Dmic.Exceeded.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_DicDoubleTheLimit_ExcessRatioIsOne()
    {
        var indicators = new ContinuityIndicators(TimeSpan.FromHours(20), 0, TimeSpan.Zero);

        var result = ContinuityComplianceEvaluator.Evaluate(indicators, Limits);

        result.Dic.Exceeded.Should().BeTrue();
        result.Dic.ExcessRatio.Should().BeApproximately(1.0, 1e-9, "20h is double the 10h limit, i.e. 100% over");
    }

    [Fact]
    public void Evaluate_FicExceedsLimit_IsFlaggedIndependentlyOfDicAndDmic()
    {
        var indicators = new ContinuityIndicators(TimeSpan.Zero, 10, TimeSpan.Zero);

        var result = ContinuityComplianceEvaluator.Evaluate(indicators, Limits);

        result.Fic.Exceeded.Should().BeTrue();
        result.Dic.Exceeded.Should().BeFalse();
        result.Dmic.Exceeded.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_ExactlyAtLimit_IsNotExceeded()
    {
        var indicators = new ContinuityIndicators(TimeSpan.FromHours(10), 5, TimeSpan.FromHours(3));

        var result = ContinuityComplianceEvaluator.Evaluate(indicators, Limits);

        result.AnyExceeded.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_ZeroLimitWithAnyApurado_IsExceeded()
    {
        var zeroLimits = new ContinuityComplianceLimits(TimeSpan.Zero, 0, TimeSpan.Zero);
        var indicators = new ContinuityIndicators(TimeSpan.FromMinutes(1), 1, TimeSpan.FromMinutes(1));

        var result = ContinuityComplianceEvaluator.Evaluate(indicators, zeroLimits);

        result.AnyExceeded.Should().BeTrue();
    }
}
