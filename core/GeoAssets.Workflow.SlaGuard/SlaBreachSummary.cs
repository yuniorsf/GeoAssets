namespace GeoAssets.Workflow.SlaGuard;

/// <summary>
/// Aggregate result of <see cref="ISlaBreachQueryService.GetLiveSummaryAsync"/> (XD01-233):
/// a live breach count and per-service-category breakdown, derived entirely from whatever
/// <see cref="SlaGuardEngine.AttachDeadline"/> has already stamped on <c>IServiceOrder</c>
/// attributes — no new persisted state.
/// </summary>
public sealed record SlaBreachSummary(
    bool IsEngineConfigured,
    int TotalBreaches,
    IReadOnlyDictionary<string, int> BreachesByServiceCategory,
    IReadOnlyList<SlaBreachEvent> Breaches)
{
    /// <summary>
    /// Returned instead of a zero-breach result when no host has registered a
    /// <see cref="SlaGuardEngine"/> with a real regulatory matrix — distinguishes
    /// "checked and found nothing" from "cannot check yet," which a plain empty summary
    /// would otherwise hide.
    /// </summary>
    public static SlaBreachSummary NotConfigured { get; } =
        new(IsEngineConfigured: false, TotalBreaches: 0, new Dictionary<string, int>(StringComparer.Ordinal), []);
}
