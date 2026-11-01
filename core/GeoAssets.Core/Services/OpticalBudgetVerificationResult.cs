namespace GeoAssets.Core.Services;

/// <summary>
/// Outcome of <see cref="OpticalBudgetVerificationService.Verify"/>, with the specific violated
/// bound named in <see cref="ViolationDetail"/> when it fails (XD01-274's AC).
/// </summary>
/// <param name="Outcome">The failure category, or <see cref="OpticalBudgetVerificationOutcome.Passed"/>.</param>
/// <param name="ViolationDetail">Human-readable description of which bound was violated. Null when <see cref="Passed"/>.</param>
/// <param name="RequiresUpstreamCtoRepair">
/// True only when the CTO reading fell <em>below</em> <see cref="OpticalBudgetVerificationService.CtoMinDbm"/>
/// specifically (not for an above-max CTO reading, nor for any PTO or insertion-loss failure) —
/// the one condition XD01-274's AC says should auto-create an upstream repair work order. A weak
/// CTO signal implicates the feeding plant upstream of the drop being tested; the other failure
/// modes are local to this drop and don't.
/// </param>
public sealed record OpticalBudgetVerificationResult(
    OpticalBudgetVerificationOutcome Outcome,
    string? ViolationDetail = null,
    bool RequiresUpstreamCtoRepair = false)
{
    public bool Passed => Outcome == OpticalBudgetVerificationOutcome.Passed;
}
