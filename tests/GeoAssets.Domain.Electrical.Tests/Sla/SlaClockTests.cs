using FluentAssertions;
using GeoAssets.Domain.Electrical.Sla;
using Xunit;

namespace GeoAssets.Domain.Electrical.Tests.Sla;

public class SlaClockTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly SlaDefinition TenDayDeadline = new("connection-request", TimeSpan.FromDays(10));

    [Fact]
    public void Status_WellBeforeDeadline_IsOnTrack()
    {
        var clock = new SlaClock(TenDayDeadline, Start);

        clock.Status(Start + TimeSpan.FromDays(1)).Should().Be(SlaClockStatus.OnTrack);
    }

    [Fact]
    public void Status_PastAtRiskThresholdButBeforeDeadline_IsAtRisk()
    {
        var clock = new SlaClock(TenDayDeadline, Start);

        clock.Status(Start + TimeSpan.FromDays(9), atRiskThreshold: 0.8).Should().Be(SlaClockStatus.AtRisk);
    }

    [Fact]
    public void Status_PastDeadline_IsOverdue()
    {
        var clock = new SlaClock(TenDayDeadline, Start);

        clock.Status(Start + TimeSpan.FromDays(11)).Should().Be(SlaClockStatus.Overdue);
    }

    [Fact]
    public void DueAt_IsStartPlusDeadline()
    {
        var clock = new SlaClock(TenDayDeadline, Start);

        clock.DueAt.Should().Be(Start + TimeSpan.FromDays(10));
    }

    [Fact]
    public void Remaining_BecomesNegative_OnceOverdue()
    {
        var clock = new SlaClock(TenDayDeadline, Start);

        clock.Remaining(Start + TimeSpan.FromDays(12)).Should().Be(TimeSpan.FromDays(-2));
    }

    [Fact]
    public void Elapsed_BeforeStart_ClampsToZero()
    {
        var clock = new SlaClock(TenDayDeadline, Start);

        clock.Elapsed(Start - TimeSpan.FromHours(1)).Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void PercentElapsed_AtHalfway_IsOneHalf()
    {
        var clock = new SlaClock(TenDayDeadline, Start);

        clock.PercentElapsed(Start + TimeSpan.FromDays(5)).Should().BeApproximately(0.5, 1e-9);
    }

    [Fact]
    public void PercentElapsed_PastDeadline_ClampsToOne()
    {
        var clock = new SlaClock(TenDayDeadline, Start);

        clock.PercentElapsed(Start + TimeSpan.FromDays(20)).Should().Be(1.0);
    }

    [Fact]
    public void IsOverdue_ExactlyAtDeadline_IsFalse()
    {
        var clock = new SlaClock(TenDayDeadline, Start);

        clock.IsOverdue(clock.DueAt).Should().BeFalse("the deadline instant itself is still on time, not late");
    }
}
