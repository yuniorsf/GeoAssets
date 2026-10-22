using FluentAssertions;
using GeoAssets.IpBilling.Calculation;
using Xunit;

namespace GeoAssets.IpBilling.Tests;

public class IpEnergyMathTests
{
    // ── Daily burn-time rate constants ───────────────────────────────────────

    [Fact]
    public void NormalLampDailyBurnTime_Is11Hours52Minutes()
    {
        IpEnergyMath.NormalLampDailyBurnTime.Should().Be(new TimeSpan(11, 52, 0));
    }

    [Fact]
    public void Irregular24hDailyBurnTime_Is12Hours8Minutes()
    {
        IpEnergyMath.Irregular24hDailyBurnTime.Should().Be(new TimeSpan(12, 8, 0));
    }

    [Fact]
    public void NormalLampDailyHours_RoundsToFourDecimals()
    {
        // 11 + 52/60 = 11.8666... -> rounds to 11.8667
        IpEnergyMath.NormalLampDailyHours.Should().Be(11.8667m);
    }

    [Fact]
    public void Irregular24hDailyHours_RoundsToFourDecimals()
    {
        // 12 + 8/60 = 12.1333... -> rounds to 12.1333
        IpEnergyMath.Irregular24hDailyHours.Should().Be(12.1333m);
    }

    // ── Rounding ──────────────────────────────────────────────────────────────
    // Decimal isn't a valid attribute-argument type, so these are plain Facts rather than a
    // Theory — keeps every value an exact decimal literal, with no double round-trip to risk
    // floating-point imprecision at the midpoint case this test specifically targets.

    [Fact]
    public void Round_AtMidpoint_RoundsAwayFromZero()
    {
        IpEnergyMath.Round(1.00005m).Should().Be(1.0001m);
    }

    [Fact]
    public void Round_BelowMidpoint_RoundsDown()
    {
        IpEnergyMath.Round(1.00004m).Should().Be(1.0000m);
    }

    [Fact]
    public void Round_NegativeAtMidpoint_RoundsAwayFromZero()
    {
        IpEnergyMath.Round(-1.00005m).Should().Be(-1.0001m);
    }

    // ── 730-day retroactive cap boundary ─────────────────────────────────────

    [Fact]
    public void IsWithinRetroactiveCap_ExactlyAtCapBoundary_ReturnsTrue()
    {
        var asOf      = new DateOnly(2026, 1, 1);
        var eventDate = asOf.AddDays(-IpEnergyMath.RetroactiveCapDays);

        IpEnergyMath.IsWithinRetroactiveCap(asOf, eventDate).Should().BeTrue();
    }

    [Fact]
    public void IsWithinRetroactiveCap_OneDayPastCapBoundary_ReturnsFalse()
    {
        var asOf      = new DateOnly(2026, 1, 1);
        var eventDate = asOf.AddDays(-(IpEnergyMath.RetroactiveCapDays + 1));

        IpEnergyMath.IsWithinRetroactiveCap(asOf, eventDate).Should().BeFalse();
    }

    [Fact]
    public void IsWithinRetroactiveCap_SameDay_ReturnsTrue()
    {
        var asOf = new DateOnly(2026, 1, 1);

        IpEnergyMath.IsWithinRetroactiveCap(asOf, asOf).Should().BeTrue();
    }

    [Fact]
    public void IsWithinRetroactiveCap_EventAfterAsOf_ReturnsFalse()
    {
        var asOf      = new DateOnly(2026, 1, 1);
        var eventDate = asOf.AddDays(1);

        IpEnergyMath.IsWithinRetroactiveCap(asOf, eventDate).Should().BeFalse();
    }
}
