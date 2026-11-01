namespace GeoAssets.Core.Services;

/// <summary>Result category of <see cref="OpticalBudgetVerificationService.Verify"/> (XD01-274).</summary>
public enum OpticalBudgetVerificationOutcome
{
    /// <summary>Both readings are within their documented ranges and the insertion loss is acceptable.</summary>
    Passed,

    /// <summary>The CTO reading falls outside <see cref="OpticalBudgetVerificationService.CtoMinDbm"/>/<see cref="OpticalBudgetVerificationService.CtoMaxDbm"/>.</summary>
    CtoReadingOutOfRange,

    /// <summary>The PTO reading falls outside <see cref="OpticalBudgetVerificationService.PtoMinDbm"/>/<see cref="OpticalBudgetVerificationService.PtoMaxDbm"/>.</summary>
    PtoReadingOutOfRange,

    /// <summary>
    /// Both readings are individually in range, but the CTO→PTO power drop exceeds
    /// <see cref="OpticalBudgetVerificationService.MaxInsertionLossDb"/> — a distinct failure
    /// mode from either reading being out of range on its own (XD01-274's AC).
    /// </summary>
    ExcessiveInsertionLoss,
}
