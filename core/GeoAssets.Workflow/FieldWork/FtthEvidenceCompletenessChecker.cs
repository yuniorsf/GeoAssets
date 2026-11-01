namespace GeoAssets.Workflow.FieldWork;

/// <summary>
/// XD01-278's AC: "All four mandatory photo slots must be filled before the checklist step can
/// complete." Pure domain check over <see cref="ServiceOrderFtthEvidenceAttributes.ReadCapturedSlots"/> —
/// wiring this into the actual checklist-completion transition is XD01-277's job (the end-to-end
/// flow ticket), same deferral <c>FtthDropInstallTransitioner</c>/<c>OpticalBudgetVerificationService</c>
/// already made for their own guarded edges.
/// </summary>
public static class FtthEvidenceCompletenessChecker
{
    public static readonly IReadOnlySet<FtthEvidenceSlot> RequiredSlots = new HashSet<FtthEvidenceSlot>(Enum.GetValues<FtthEvidenceSlot>());

    public static bool IsComplete(IReadOnlyDictionary<string, string> attributes) =>
        MissingSlots(attributes).Count == 0;

    /// <summary>The required slots not yet present in <paramref name="attributes"/>, in declaration order.</summary>
    public static IReadOnlyList<FtthEvidenceSlot> MissingSlots(IReadOnlyDictionary<string, string> attributes)
    {
        var captured = ServiceOrderFtthEvidenceAttributes.ReadCapturedSlots(attributes);
        return RequiredSlots.Where(slot => !captured.Contains(slot)).ToList();
    }
}
