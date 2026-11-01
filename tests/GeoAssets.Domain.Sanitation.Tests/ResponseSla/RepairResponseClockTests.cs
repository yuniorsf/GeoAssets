using FluentAssertions;
using GeoAssets.Domain.Sanitation.ResponseSla;
using Xunit;

namespace GeoAssets.Domain.Sanitation.Tests.ResponseSla;

public class RepairResponseClockTests
{
    private static readonly DateTimeOffset Reported = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly RepairResponseDefinition VisibleLeak = new("visible-leak", TimeSpan.FromHours(24));

    [Fact]
    public void Status_WellBeforeDeadline_IsOnTrack()
    {
        var clock = new RepairResponseClock(VisibleLeak, Reported);

        clock.Status(Reported + TimeSpan.FromHours(2)).Should().Be(RepairResponseStatus.OnTrack);
    }

    [Fact]
    public void Status_PastAtRiskThresholdButBeforeDeadline_IsAtRisk()
    {
        var clock = new RepairResponseClock(VisibleLeak, Reported);

        clock.Status(Reported + TimeSpan.FromHours(20), atRiskThreshold: 0.8).Should().Be(RepairResponseStatus.AtRisk);
    }

    [Fact]
    public void Status_PastDeadline_IsOverdue()
    {
        var clock = new RepairResponseClock(VisibleLeak, Reported);

        clock.Status(Reported + TimeSpan.FromHours(25)).Should().Be(RepairResponseStatus.Overdue);
    }

    [Fact]
    public void DueAt_IsReportedPlusDeadline()
    {
        var clock = new RepairResponseClock(VisibleLeak, Reported);

        clock.DueAt.Should().Be(Reported + TimeSpan.FromHours(24));
    }

    [Fact]
    public void Remaining_BecomesNegative_OnceOverdue()
    {
        var clock = new RepairResponseClock(VisibleLeak, Reported);

        clock.Remaining(Reported + TimeSpan.FromHours(26)).Should().Be(TimeSpan.FromHours(-2));
    }

    [Fact]
    public void Elapsed_BeforeReported_ClampsToZero()
    {
        var clock = new RepairResponseClock(VisibleLeak, Reported);

        clock.Elapsed(Reported - TimeSpan.FromMinutes(5)).Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void PercentElapsed_AtHalfway_IsOneHalf()
    {
        var clock = new RepairResponseClock(VisibleLeak, Reported);

        clock.PercentElapsed(Reported + TimeSpan.FromHours(12)).Should().BeApproximately(0.5, 1e-9);
    }

    [Fact]
    public void IsOverdue_ExactlyAtDeadline_IsFalse()
    {
        var clock = new RepairResponseClock(VisibleLeak, Reported);

        clock.IsOverdue(clock.DueAt).Should().BeFalse("the deadline instant itself is still on time, not late");
    }
}
