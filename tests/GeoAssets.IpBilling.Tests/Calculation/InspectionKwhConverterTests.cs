using FluentAssertions;
using GeoAssets.IpBilling.Calculation;
using GeoAssets.IpBilling.Models;
using Xunit;

namespace GeoAssets.IpBilling.Tests.Calculation;

/// <summary>
/// Covers every guard/branch of <see cref="InspectionKwhConverter"/> (XD01-301) — the IDs in
/// each test name (KWH-*, RET-*) are the ones in the 2A contract.
/// </summary>
public class InspectionKwhConverterTests
{
    private const decimal NormalHours = 11.8667m;
    private const decimal Irregular24hHours = 12.1333m;

    private static InspectionLampChart Chart(decimal? standard = null, params (LampTypeCode Code, int WattageW, decimal TotalBilledW)[] rows) =>
        new(rows.ToDictionary(r => (r.Code, r.WattageW), r => r.TotalBilledW), standard);

    private static InspectionPointEntry Exact(int quantity, int sign = 1, bool irregular24h = false, bool retro = true) =>
        new(quantity, sign, irregular24h, retro, LampTypeCode.VS, 100);

    private static InspectionPointEntry Lampless(int quantity, int sign = 1, bool irregular24h = false, bool retro = true) =>
        new(quantity, sign, irregular24h, retro);

    private static readonly InspectionLampChart Vs100At117 = Chart(null, (LampTypeCode.VS, 100, 117m));

    // ── Guards ────────────────────────────────────────────────────────────────

    [Fact]
    public void Convert_KWH_G1_NullEntries_Throws()
    {
        var act = () => InspectionKwhConverter.Convert(null!, Vs100At117, 30, 0);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Convert_KWH_G1_NullChart_Throws()
    {
        var act = () => InspectionKwhConverter.Convert([], null!, 30, 0);

        act.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Convert_KWH_G2_NonPositiveDaysInCompetencia_Throws(int days)
    {
        var act = () => InspectionKwhConverter.Convert([], Vs100At117, days, 0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(731)]
    public void Convert_KWH_G3_RetroactiveDaysOutOfRange_Throws(int retroactiveDays)
    {
        var act = () => InspectionKwhConverter.Convert([], Vs100At117, 30, retroactiveDays);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(730)]
    public void Convert_KWH_G3_RetroactiveDaysAtTheBounds_AreAccepted(int retroactiveDays)
    {
        var result = InspectionKwhConverter.Convert([], Vs100At117, 30, retroactiveDays);

        result.RetroactiveDays.Should().Be(retroactiveDays);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void Convert_KWH_G4_NonPositiveQuantity_Throws(int quantity)
    {
        var act = () => InspectionKwhConverter.Convert([Exact(quantity)], Vs100At117, 30, 0);

        act.Should().Throw<ArgumentException>().WithMessage("*quantity*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(-2)]
    public void Convert_KWH_G5_SignNotPlusOrMinusOne_Throws(int sign)
    {
        var act = () => InspectionKwhConverter.Convert([Exact(1, sign)], Vs100At117, 30, 0);

        act.Should().Throw<ArgumentException>().WithMessage("*sign*");
    }

    [Fact]
    public void Convert_KWH_G6_LampCodeWithoutWattage_Throws()
    {
        var entry = new InspectionPointEntry(1, 1, false, true, LampTypeCode.VS, null);

        var act = () => InspectionKwhConverter.Convert([entry], Vs100At117, 30, 0);

        act.Should().Throw<ArgumentException>().WithMessage("*both present or both absent*");
    }

    [Fact]
    public void Convert_KWH_G6_WattageWithoutLampCode_Throws()
    {
        var entry = new InspectionPointEntry(1, 1, false, true, null, 100);

        var act = () => InspectionKwhConverter.Convert([entry], Vs100At117, 30, 0);

        act.Should().Throw<ArgumentException>().WithMessage("*both present or both absent*");
    }

    [Fact]
    public void Convert_NullEntryInList_Throws()
    {
        var act = () => InspectionKwhConverter.Convert([null!], Vs100At117, 30, 0);

        act.Should().Throw<ArgumentNullException>();
    }

    // ── Branches ──────────────────────────────────────────────────────────────

    [Fact]
    public void Convert_KWH_B1_LampEntryInChart_IsExact_WorkedExample()
    {
        // 10 × 117 W × 11.8667 h × 30 d ÷ 1000 = 416.52117 → 416.5212 (the contract's worked example).
        var result = InspectionKwhConverter.Convert([Exact(10, retro: false)], Vs100At117, 30, 0);

        result.MonthlyKwh.Should().Be(416.5212m);
        result.RetroactiveKwh.Should().Be(0m);
        result.DeltaKwh.Should().Be(416.5212m);
        result.ExactPoints.Should().Be(10);
        result.ProvisionalPoints.Should().Be(0);
        result.IsProvisional.Should().BeFalse();
        result.StandardWattsPerPoint.Should().BeNull("no provisional point used the standard value");
    }

    [Fact]
    public void Convert_KWH_B2_LampEntryNotInChart_ThrowsCarryingCodeAndWattage()
    {
        var entry = new InspectionPointEntry(3, 1, false, true, LampTypeCode.LD, 60);

        var act = () => InspectionKwhConverter.Convert([entry], Vs100At117, 30, 0);

        var ex = act.Should().Throw<InspectionLampNotInChartException>().Which;
        ex.Code.Should().Be(LampTypeCode.LD);
        ex.WattageW.Should().Be(60);
        ex.Message.Should().Contain("LD").And.Contain("60");
    }

    [Fact]
    public void Convert_KWH_B3_LamplessEntryWithStandardValue_IsProvisional_AndEchoesTheStandard()
    {
        var result = InspectionKwhConverter.Convert([Lampless(10, retro: false)], Chart(standard: 117m), 30, 0);

        result.MonthlyKwh.Should().Be(416.5212m);
        result.IsProvisional.Should().BeTrue();
        result.ProvisionalPoints.Should().Be(10);
        result.ExactPoints.Should().Be(0);
        result.StandardWattsPerPoint.Should().Be(117m);
    }

    [Fact]
    public void Convert_KWH_B4_LamplessEntryWithoutStandardValue_Throws()
    {
        var act = () => InspectionKwhConverter.Convert([Lampless(1)], Vs100At117, 30, 0);

        act.Should().Throw<InspectionStandardWattageUnavailableException>();
    }

    [Fact]
    public void Convert_KWH_B5_Irregular24hUsesItsOwnBurnHours()
    {
        var normal = InspectionKwhConverter.Convert([Exact(2, retro: false)], Chart(null, (LampTypeCode.VS, 100, 100m)), 30, 0);
        var irregular = InspectionKwhConverter.Convert(
            [Exact(2, irregular24h: true, retro: false)], Chart(null, (LampTypeCode.VS, 100, 100m)), 30, 0);

        normal.MonthlyKwh.Should().Be(2 * 100m * NormalHours * 30 / 1000m);        // 71.2002
        irregular.MonthlyKwh.Should().Be(2 * 100m * Irregular24hHours * 30 / 1000m); // 72.7998
        normal.MonthlyKwh.Should().Be(71.2002m);
        irregular.MonthlyKwh.Should().Be(72.7998m);
    }

    [Fact]
    public void Convert_KWH_B6_OnlyRetroactiveEntriesContributeToRetroactiveKwh()
    {
        var chart = Chart(null, (LampTypeCode.VS, 100, 100m));

        var result = InspectionKwhConverter.Convert(
            [Exact(1, retro: true), Exact(1, retro: false)], chart, daysInCompetencia: 30, retroactiveDays: 10);

        result.MonthlyKwh.Should().Be(71.2002m, "both entries bill the competência");
        result.RetroactiveKwh.Should().Be(11.8667m, "only the retroactive entry bills the 10 retroactive days");
        result.DeltaKwh.Should().Be(83.0669m);
        result.RetroactiveDays.Should().Be(10);
    }

    [Fact]
    public void Convert_KWH_B7_NegativeSign_YieldsNegativeMonthlyAndRetro_NotClamped()
    {
        var chart = Chart(null, (LampTypeCode.VS, 100, 100m));

        var result = InspectionKwhConverter.Convert([Exact(1, sign: -1)], chart, 30, 10);

        result.MonthlyKwh.Should().Be(-35.6001m);
        result.RetroactiveKwh.Should().Be(-11.8667m);
        result.DeltaKwh.Should().Be(-47.4668m);
    }

    [Fact]
    public void Convert_KWH_B8_NoEntries_IsAllZerosAndNotProvisional()
    {
        var result = InspectionKwhConverter.Convert([], Chart(standard: 117m), 30, 10);

        result.Should().Be(new InspectionKwhResult(0m, 0m, 0m, false, 0, 0, null, 10));
    }

    [Fact]
    public void Convert_KWH_B9_MixedExactAndProvisional_ReportsBothCounts()
    {
        var chart = Chart(80m, (LampTypeCode.VS, 100, 117m));

        var result = InspectionKwhConverter.Convert([Exact(4), Lampless(6), Lampless(1, sign: -1)], chart, 30, 0);

        result.ExactPoints.Should().Be(4);
        result.ProvisionalPoints.Should().Be(7, "points are counted unsigned");
        result.IsProvisional.Should().BeTrue();
        result.StandardWattsPerPoint.Should().Be(80m);
    }

    [Fact]
    public void Convert_KWH_B10_RoundsEachComponentAndTheSum_To4Dp_AwayFromZero()
    {
        // 1 × 1.0001 W × 11.8667 h × 1 d ÷ 1000 = 0.01186788…; ×-1 → -0.0119 (not -0.0118).
        var chart = Chart(null, (LampTypeCode.VS, 100, 1.0001m));

        var result = InspectionKwhConverter.Convert([Exact(1, sign: -1)], chart, daysInCompetencia: 1, retroactiveDays: 1);

        result.MonthlyKwh.Should().Be(-0.0119m);
        result.RetroactiveKwh.Should().Be(-0.0119m);
        result.DeltaKwh.Should().Be(-0.0238m, "the delta is the rounded sum of the two rounded components");
    }

    [Fact]
    public void Convert_KWH_B10_AccumulatesUnroundedBeforeRounding()
    {
        // Each entry is 0.00004 kWh — rounding per entry would give 0; the unrounded sum is 0.0002.
        var chart = Chart(null, (LampTypeCode.VS, 100, 0.0033710m));
        var entries = Enumerable.Repeat(Exact(1, retro: false), 5).ToList();

        var result = InspectionKwhConverter.Convert(entries, chart, 1, 0);

        result.MonthlyKwh.Should().Be(0.0002m);
    }

    // ── RetroactiveDays ───────────────────────────────────────────────────────

    [Fact]
    public void RetroactiveDays_RET_B1_NoAnchor_IsZero() =>
        InspectionKwhConverter.RetroactiveDays(null, new DateOnly(2027, 1, 1)).Should().Be(0);

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public void RetroactiveDays_RET_B2_AnchorOnOrAfterAsOf_IsZero(int daysAfter)
    {
        var asOf = new DateOnly(2027, 1, 1);

        InspectionKwhConverter.RetroactiveDays(asOf.AddDays(daysAfter), asOf).Should().Be(0);
    }

    [Fact]
    public void RetroactiveDays_RET_B3_Exactly730Days_IsWithinTheCap()
    {
        var asOf = new DateOnly(2027, 1, 1);

        InspectionKwhConverter.RetroactiveDays(asOf.AddDays(-730), asOf).Should().Be(730);
    }

    [Fact]
    public void RetroactiveDays_RET_B4_731Days_IsCappedAt730()
    {
        var asOf = new DateOnly(2027, 1, 1);

        InspectionKwhConverter.RetroactiveDays(asOf.AddDays(-731), asOf).Should().Be(730);
    }

    [Fact]
    public void RetroactiveDays_RET_B5_28Days_Is28() =>
        InspectionKwhConverter.RetroactiveDays(new DateOnly(2027, 2, 1), new DateOnly(2027, 3, 1)).Should().Be(28);
}
