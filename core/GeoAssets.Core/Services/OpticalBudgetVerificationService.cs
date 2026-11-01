using System.Globalization;

namespace GeoAssets.Core.Services;

/// <summary>
/// Validates an FTTH drop's optical power meter (OPM) readings against the documented budget
/// (XD01-274): CTO reading, PTO reading, and the CTO→PTO insertion loss they imply. Pure domain
/// service with no <c>ServiceOrder</c>/workflow dependency — a consumer (e.g. the FTTH
/// drop-install workflow guard, or the mobile checklist) decides what to do with the result,
/// including whether to act on <see cref="OpticalBudgetVerificationResult.RequiresUpstreamCtoRepair"/>
/// (see <c>GeoAssets.Workflow.FieldWork.UpstreamCtoRepairOrderSpawner</c>).
/// </summary>
public static class OpticalBudgetVerificationService
{
    /// <summary>Documented minimum acceptable CTO reading, in dBm.</summary>
    public const double CtoMinDbm = -22.0;

    /// <summary>Documented maximum acceptable CTO reading, in dBm.</summary>
    public const double CtoMaxDbm = -15.0;

    /// <summary>Documented minimum acceptable PTO reading, in dBm.</summary>
    public const double PtoMinDbm = -25.0;

    /// <summary>Documented maximum acceptable PTO reading, in dBm.</summary>
    public const double PtoMaxDbm = -15.0;

    /// <summary>Documented maximum acceptable CTO→PTO insertion loss, in dB.</summary>
    public const double MaxInsertionLossDb = 0.5;

    /// <summary>
    /// Checks <paramref name="ctoReadingDbm"/>/<paramref name="ptoReadingDbm"/> against their
    /// documented ranges first (CTO before PTO), then the insertion loss they imply
    /// (<paramref name="ctoReadingDbm"/> minus <paramref name="ptoReadingDbm"/> — power always
    /// drops between the two, so this is positive in the passing case). Checks are short-circuit:
    /// an out-of-range CTO reading is reported on its own, without also evaluating insertion loss
    /// against a reading already known to be invalid.
    /// </summary>
    public static OpticalBudgetVerificationResult Verify(double ctoReadingDbm, double ptoReadingDbm)
    {
        if (ctoReadingDbm < CtoMinDbm)
            return new OpticalBudgetVerificationResult(
                OpticalBudgetVerificationOutcome.CtoReadingOutOfRange,
                string.Create(CultureInfo.InvariantCulture, $"CTO reading {ctoReadingDbm:F2} dBm is below the documented minimum of {CtoMinDbm:F2} dBm."),
                RequiresUpstreamCtoRepair: true);

        if (ctoReadingDbm > CtoMaxDbm)
            return new OpticalBudgetVerificationResult(
                OpticalBudgetVerificationOutcome.CtoReadingOutOfRange,
                string.Create(CultureInfo.InvariantCulture, $"CTO reading {ctoReadingDbm:F2} dBm is above the documented maximum of {CtoMaxDbm:F2} dBm."));

        if (ptoReadingDbm < PtoMinDbm)
            return new OpticalBudgetVerificationResult(
                OpticalBudgetVerificationOutcome.PtoReadingOutOfRange,
                string.Create(CultureInfo.InvariantCulture, $"PTO reading {ptoReadingDbm:F2} dBm is below the documented minimum of {PtoMinDbm:F2} dBm."));

        if (ptoReadingDbm > PtoMaxDbm)
            return new OpticalBudgetVerificationResult(
                OpticalBudgetVerificationOutcome.PtoReadingOutOfRange,
                string.Create(CultureInfo.InvariantCulture, $"PTO reading {ptoReadingDbm:F2} dBm is above the documented maximum of {PtoMaxDbm:F2} dBm."));

        var insertionLossDb = ctoReadingDbm - ptoReadingDbm;
        if (insertionLossDb > MaxInsertionLossDb)
            return new OpticalBudgetVerificationResult(
                OpticalBudgetVerificationOutcome.ExcessiveInsertionLoss,
                string.Create(CultureInfo.InvariantCulture, $"Drop insertion loss {insertionLossDb:F2} dB exceeds the documented maximum of {MaxInsertionLossDb:F2} dB."));

        return new OpticalBudgetVerificationResult(OpticalBudgetVerificationOutcome.Passed);
    }
}
