using FluentAssertions;
using GeoAssets.Domain.Electrical.Continuity;
using Xunit;

namespace GeoAssets.Domain.Electrical.Tests.Continuity;

public class ContinuityIndicatorsCalculatorTests
{
    private const string Unit = "UC-1";
    private static readonly DateTimeOffset PeriodStart = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset PeriodEnd = PeriodStart + TimeSpan.FromDays(30);

    [Fact]
    public void Calculate_NoEvents_ReturnsZeroIndicators()
    {
        var result = ContinuityIndicatorsCalculator.Calculate([], Unit, PeriodStart, PeriodEnd);

        result.Should().Be(new ContinuityIndicators(TimeSpan.Zero, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Calculate_MultipleEventsWithinPeriod_SumsDicCountsFicAndTakesMaxDmic()
    {
        var events = new[]
        {
            new InterruptionEvent(Unit, PeriodStart + TimeSpan.FromHours(1), PeriodStart + TimeSpan.FromHours(3)),  // 2h
            new InterruptionEvent(Unit, PeriodStart + TimeSpan.FromDays(5), PeriodStart + TimeSpan.FromDays(5).Add(TimeSpan.FromHours(5))), // 5h — the longest
            new InterruptionEvent(Unit, PeriodStart + TimeSpan.FromDays(10), PeriodStart + TimeSpan.FromDays(10).Add(TimeSpan.FromMinutes(30))), // 0.5h
        };

        var result = ContinuityIndicatorsCalculator.Calculate(events, Unit, PeriodStart, PeriodEnd);

        result.Dic.Should().Be(TimeSpan.FromHours(7.5));
        result.Fic.Should().Be(3);
        result.Dmic.Should().Be(TimeSpan.FromHours(5));
    }

    [Fact]
    public void Calculate_EventForDifferentConsumerUnit_IsExcluded()
    {
        var events = new[] { new InterruptionEvent("UC-OTHER", PeriodStart + TimeSpan.FromHours(1), PeriodStart + TimeSpan.FromHours(3)) };

        var result = ContinuityIndicatorsCalculator.Calculate(events, Unit, PeriodStart, PeriodEnd);

        result.Fic.Should().Be(0);
    }

    [Fact]
    public void Calculate_EventEntirelyOutsidePeriod_IsExcluded()
    {
        var events = new[] { new InterruptionEvent(Unit, PeriodEnd + TimeSpan.FromHours(1), PeriodEnd + TimeSpan.FromHours(2)) };

        var result = ContinuityIndicatorsCalculator.Calculate(events, Unit, PeriodStart, PeriodEnd);

        result.Fic.Should().Be(0);
        result.Dic.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void Calculate_EventStraddlingPeriodStart_OnlyCountsThePortionInsideThePeriod()
    {
        var events = new[] { new InterruptionEvent(Unit, PeriodStart - TimeSpan.FromHours(1), PeriodStart + TimeSpan.FromHours(2)) };

        var result = ContinuityIndicatorsCalculator.Calculate(events, Unit, PeriodStart, PeriodEnd);

        result.Dic.Should().Be(TimeSpan.FromHours(2), "only the 2 hours inside [periodStart, periodEnd) should count, not the hour before it");
        result.Fic.Should().Be(1);
    }

    [Fact]
    public void Calculate_EventStraddlingPeriodEnd_OnlyCountsThePortionInsideThePeriod()
    {
        var events = new[] { new InterruptionEvent(Unit, PeriodEnd - TimeSpan.FromHours(1), PeriodEnd + TimeSpan.FromHours(3)) };

        var result = ContinuityIndicatorsCalculator.Calculate(events, Unit, PeriodStart, PeriodEnd);

        result.Dic.Should().Be(TimeSpan.FromHours(1));
    }

    [Fact]
    public void Calculate_MinimumQualifyingDurationSet_ExcludesShorterEventsByTheirFullUnclippedDuration()
    {
        var events = new[]
        {
            new InterruptionEvent(Unit, PeriodStart, PeriodStart + TimeSpan.FromMinutes(2)),  // short — excluded
            new InterruptionEvent(Unit, PeriodStart + TimeSpan.FromHours(1), PeriodStart + TimeSpan.FromHours(1).Add(TimeSpan.FromMinutes(10))), // qualifies
        };

        var result = ContinuityIndicatorsCalculator.Calculate(events, Unit, PeriodStart, PeriodEnd, minimumQualifyingDuration: TimeSpan.FromMinutes(3));

        result.Fic.Should().Be(1);
        result.Dic.Should().Be(TimeSpan.FromMinutes(10));
    }

    [Fact]
    public void Calculate_PeriodEndNotAfterStart_Throws()
    {
        var act = () => ContinuityIndicatorsCalculator.Calculate([], Unit, PeriodStart, PeriodStart);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
