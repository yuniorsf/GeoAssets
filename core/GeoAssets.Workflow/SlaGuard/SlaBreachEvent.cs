namespace GeoAssets.Workflow.SlaGuard;

/// <summary>
/// Raised by <see cref="SlaGuardEngine.CheckBreach"/> when a service order's statutory deadline
/// has passed (XD01-226) — a typed domain event, consumable by downstream reporting, rather than
/// a bare bool the caller has to separately decide how to log/report.
/// </summary>
public sealed record SlaBreachEvent(
    string OrderId,
    string ServiceCategory,
    string TerritorialSetting,
    DateTimeOffset Deadline,
    DateTimeOffset DetectedAt)
{
    /// <summary>How far past the deadline <see cref="DetectedAt"/> is — always positive, since this event only exists when a breach was detected.</summary>
    public TimeSpan Overage => DetectedAt - Deadline;
}
