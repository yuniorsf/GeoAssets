namespace GeoAssets.Workflow.Isp.NeutralOperator;

/// <summary>
/// A dispute (XD01-269) currently awaiting mediation, as of the call that produced it —
/// the read surface XD01-270's overdue-dispute query returns to the neutral operator's
/// mediation team.
/// </summary>
public sealed record ClandestineDisputeResult(
    string ServiceOrderId,
    Guid OrganizationId,
    DateTimeOffset DisputeSubmittedAtUtc,
    DateTimeOffset MediationDeadlineUtc);
