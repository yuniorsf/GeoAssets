namespace GeoAssets.Shared.Services.FieldWork;

/// <summary>
/// Outcome of <see cref="PoleCensusInspectionService.RecordSubmission"/> (XD01-260). AC: "A
/// completed census form validates against <c>PoleSharingComplianceChecklistSchema</c> before
/// local queuing" — <see cref="Explanation"/> carries the first schema-validation error on
/// failure, the same "validate, don't throw, surface it to the technician" shape
/// <c>FtthChecklistStepResult</c> already established for the FTTH checklist (XD01-277).
/// </summary>
public sealed record PoleCensusChecklistStepResult(bool Succeeded, string? Explanation)
{
    public static PoleCensusChecklistStepResult Success() => new(true, null);

    public static PoleCensusChecklistStepResult Failure(string explanation) => new(false, explanation);
}
