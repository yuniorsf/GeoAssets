using System.Globalization;
using FluentAssertions;
using GeoAssets.Core.Services;
using Xunit;

namespace GeoAssets.Core.Tests.Services;

/// <summary>
/// XD01-274's AC: documented-bound rejection with the specific violated bound named, a distinct
/// "excessive insertion loss" outcome, and boundary-value coverage (exactly at threshold, just
/// inside, just outside).
/// </summary>
public class OpticalBudgetVerificationServiceTests
{
    private const double MidRangeCto = -18.0;
    private const double MidRangePto = -18.3; // 0.3 dB loss — within budget

    [Fact]
    public void Verify_BothReadingsMidRangeWithSmallLoss_Passes()
    {
        var result = OpticalBudgetVerificationService.Verify(MidRangeCto, MidRangePto);

        result.Passed.Should().BeTrue();
        result.Outcome.Should().Be(OpticalBudgetVerificationOutcome.Passed);
        result.ViolationDetail.Should().BeNull();
        result.RequiresUpstreamCtoRepair.Should().BeFalse();
    }

    [Theory]
    [InlineData(OpticalBudgetVerificationService.CtoMinDbm)]
    [InlineData(OpticalBudgetVerificationService.CtoMaxDbm)]
    public void Verify_CtoReadingExactlyAtBoundary_Passes(double ctoReadingDbm)
    {
        // PTO set just below the CTO reading so insertion loss stays within budget regardless of which boundary is exercised.
        var result = OpticalBudgetVerificationService.Verify(ctoReadingDbm, ctoReadingDbm - 0.2);

        result.Passed.Should().BeTrue();
    }

    [Fact]
    public void Verify_CtoReadingJustBelowMinimum_FailsWithOutOfRangeAndRequiresUpstreamRepair()
    {
        var ctoReadingDbm = OpticalBudgetVerificationService.CtoMinDbm - 0.1;

        var result = OpticalBudgetVerificationService.Verify(ctoReadingDbm, -20.0);

        result.Passed.Should().BeFalse();
        result.Outcome.Should().Be(OpticalBudgetVerificationOutcome.CtoReadingOutOfRange);
        result.ViolationDetail.Should().Contain(OpticalBudgetVerificationService.CtoMinDbm.ToString("F2", CultureInfo.InvariantCulture));
        result.RequiresUpstreamCtoRepair.Should().BeTrue("the CTO reading fell below the documented minimum");
    }

    [Fact]
    public void Verify_CtoReadingJustAboveMaximum_FailsWithOutOfRangeButNoUpstreamRepair()
    {
        var ctoReadingDbm = OpticalBudgetVerificationService.CtoMaxDbm + 0.1;

        var result = OpticalBudgetVerificationService.Verify(ctoReadingDbm, -16.0);

        result.Outcome.Should().Be(OpticalBudgetVerificationOutcome.CtoReadingOutOfRange);
        result.ViolationDetail.Should().Contain(OpticalBudgetVerificationService.CtoMaxDbm.ToString("F2", CultureInfo.InvariantCulture));
        result.RequiresUpstreamCtoRepair.Should().BeFalse("only a below-minimum CTO reading implicates the upstream feed");
    }

    [Fact]
    public void Verify_PtoReadingExactlyAtMaximum_WithMatchingCtoReading_Passes()
    {
        // PtoMaxDbm and CtoMaxDbm are the same documented value (-15.0); pinning both readings
        // there gives zero insertion loss, isolating the PTO-maximum boundary itself.
        var result = OpticalBudgetVerificationService.Verify(OpticalBudgetVerificationService.CtoMaxDbm, OpticalBudgetVerificationService.PtoMaxDbm);

        result.Passed.Should().BeTrue();
    }

    [Fact]
    public void Verify_PtoReadingExactlyAtMinimum_IsNotRejectedAsOutOfRange()
    {
        // CtoMinDbm/PtoMinDbm are 3 dB apart — further than the insertion-loss budget allows —
        // so the overall result still fails, but on ExcessiveInsertionLoss, not on the PTO range
        // check itself. This isolates that the PTO range check treats its exact minimum as valid.
        var result = OpticalBudgetVerificationService.Verify(OpticalBudgetVerificationService.CtoMinDbm, OpticalBudgetVerificationService.PtoMinDbm);

        result.Outcome.Should().NotBe(OpticalBudgetVerificationOutcome.PtoReadingOutOfRange);
    }

    [Fact]
    public void Verify_PtoReadingJustBelowMinimum_FailsWithOutOfRange()
    {
        var ptoReadingDbm = OpticalBudgetVerificationService.PtoMinDbm - 0.1;

        var result = OpticalBudgetVerificationService.Verify(MidRangeCto, ptoReadingDbm);

        result.Outcome.Should().Be(OpticalBudgetVerificationOutcome.PtoReadingOutOfRange);
        result.ViolationDetail.Should().Contain(OpticalBudgetVerificationService.PtoMinDbm.ToString("F2", CultureInfo.InvariantCulture));
        result.RequiresUpstreamCtoRepair.Should().BeFalse();
    }

    [Fact]
    public void Verify_PtoReadingJustAboveMaximum_FailsWithOutOfRange()
    {
        var ptoReadingDbm = OpticalBudgetVerificationService.PtoMaxDbm + 0.1;

        var result = OpticalBudgetVerificationService.Verify(MidRangeCto, ptoReadingDbm);

        result.Outcome.Should().Be(OpticalBudgetVerificationOutcome.PtoReadingOutOfRange);
        result.ViolationDetail.Should().Contain(OpticalBudgetVerificationService.PtoMaxDbm.ToString("F2", CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Verify_InsertionLossExactlyAtMaximum_Passes()
    {
        var ptoReadingDbm = MidRangeCto - OpticalBudgetVerificationService.MaxInsertionLossDb;

        var result = OpticalBudgetVerificationService.Verify(MidRangeCto, ptoReadingDbm);

        result.Passed.Should().BeTrue();
    }

    [Fact]
    public void Verify_InsertionLossJustAboveMaximum_FailsWithDistinctExcessiveInsertionLossOutcome()
    {
        var ptoReadingDbm = MidRangeCto - OpticalBudgetVerificationService.MaxInsertionLossDb - 0.1;

        var result = OpticalBudgetVerificationService.Verify(MidRangeCto, ptoReadingDbm);

        result.Outcome.Should().Be(OpticalBudgetVerificationOutcome.ExcessiveInsertionLoss,
            "this is a distinct failure mode from either individual reading being out of range");
        result.RequiresUpstreamCtoRepair.Should().BeFalse();
    }

    [Fact]
    public void Verify_CtoOutOfRange_DoesNotAlsoReportInsertionLoss()
    {
        // CTO below minimum AND an implied insertion loss that would independently fail too —
        // the out-of-range CTO check must win (short-circuit), not insertion loss.
        var result = OpticalBudgetVerificationService.Verify(-23.0, -30.0);

        result.Outcome.Should().Be(OpticalBudgetVerificationOutcome.CtoReadingOutOfRange);
    }
}
