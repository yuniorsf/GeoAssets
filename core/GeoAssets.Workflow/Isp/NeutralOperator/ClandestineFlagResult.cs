namespace GeoAssets.Workflow.Isp.NeutralOperator;

/// <summary>A flagged order's current clandestine-notice state, as of the call that produced it.</summary>
public sealed record ClandestineFlagResult(
    string ServiceOrderId,
    string Status,
    DateTimeOffset FlaggedAtUtc,
    DateTimeOffset DeadlineUtc);
