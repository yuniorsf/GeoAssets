namespace GeoAssets.Domain.Sanitation.ResponseSla;

/// <summary>
/// A running countdown against one <see cref="RepairResponseDefinition"/>, started at
/// <see cref="ReportedAt"/>. Every computation below takes an explicit <paramref name="asOf"/>
/// instant rather than reading a clock itself, so callers inject their own
/// <see cref="TimeProvider"/> (per this codebase's <c>DateTime.UtcNow</c>/<c>DateTime.Now</c>
/// ban) and this type stays trivially unit-testable.
/// </summary>
public sealed record RepairResponseClock(RepairResponseDefinition Definition, DateTimeOffset ReportedAt)
{
    public DateTimeOffset DueAt => ReportedAt + Definition.Deadline;

    /// <summary>Elapsed time since <see cref="ReportedAt"/>, clamped to never go negative.</summary>
    public TimeSpan Elapsed(DateTimeOffset asOf) => asOf < ReportedAt ? TimeSpan.Zero : asOf - ReportedAt;

    /// <summary>Time left until <see cref="DueAt"/>. Negative once overdue.</summary>
    public TimeSpan Remaining(DateTimeOffset asOf) => DueAt - asOf;

    public bool IsOverdue(DateTimeOffset asOf) => asOf > DueAt;

    /// <summary>Elapsed time as a fraction of the total deadline, clamped to [0, 1]; stays at 1 once overdue.</summary>
    public double PercentElapsed(DateTimeOffset asOf)
    {
        if (Definition.Deadline <= TimeSpan.Zero) return 1.0;
        var fraction = Elapsed(asOf).TotalSeconds / Definition.Deadline.TotalSeconds;
        return Math.Clamp(fraction, 0.0, 1.0);
    }

    /// <summary>
    /// <see cref="RepairResponseStatus.Overdue"/> once past <see cref="DueAt"/>; otherwise
    /// <see cref="RepairResponseStatus.AtRisk"/> once <paramref name="atRiskThreshold"/>
    /// (fraction of the deadline elapsed, e.g. 0.8 for "80% of the time is gone") is reached,
    /// else <see cref="RepairResponseStatus.OnTrack"/>.
    /// </summary>
    public RepairResponseStatus Status(DateTimeOffset asOf, double atRiskThreshold = 0.8)
    {
        if (IsOverdue(asOf)) return RepairResponseStatus.Overdue;
        return PercentElapsed(asOf) >= atRiskThreshold ? RepairResponseStatus.AtRisk : RepairResponseStatus.OnTrack;
    }
}
