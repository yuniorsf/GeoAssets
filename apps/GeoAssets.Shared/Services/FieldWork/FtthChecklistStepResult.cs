using GeoAssets.Workflow.Orders;

namespace GeoAssets.Shared.Services.FieldWork;

/// <summary>
/// Outcome of one <see cref="FtthDropInstallChecklistService"/> step (XD01-277). AC: "Any OPM
/// failure blocks progression to the next checklist step with an on-screen explanation" —
/// <see cref="Explanation"/> is that explanation, for every step that can fail, not only the OPM
/// one; the order is left untransitioned on failure (each step mutates and transitions only once
/// its own precondition passes).
/// </summary>
/// <param name="Succeeded">Whether the step completed and the order transitioned.</param>
/// <param name="Explanation">Human-readable failure reason. Null when <see cref="Succeeded"/>.</param>
/// <param name="UpstreamRepairOrder">
/// Set only by <see cref="FtthDropInstallChecklistService.RecordPtoReadingAndVerifyBudget"/>
/// when the CTO reading requires an upstream repair order (XD01-274) — the caller is
/// responsible for persisting it; this service only builds it.
/// </param>
public sealed record FtthChecklistStepResult(bool Succeeded, string? Explanation, ServiceOrder? UpstreamRepairOrder = null)
{
    public static FtthChecklistStepResult Success(ServiceOrder? upstreamRepairOrder = null) => new(true, null, upstreamRepairOrder);

    public static FtthChecklistStepResult Failure(string explanation) => new(false, explanation);
}
