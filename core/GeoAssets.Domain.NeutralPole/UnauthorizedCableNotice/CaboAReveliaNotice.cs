namespace GeoAssets.Domain.NeutralPole.UnauthorizedCableNotice;

/// <summary>
/// One "Cabo à Revelia" (unauthorized cable) notice: Decreto 12.068/2024's 30-day cure window
/// between detecting an unauthorized attachment on a neutral pole and either its resolution or
/// its escalation past the deadline.
///
/// Every transition takes an explicit instant rather than reading a clock itself, so callers
/// inject their own <see cref="TimeProvider"/> (per this codebase's <c>DateTime.UtcNow</c>/
/// <c>DateTime.Now</c> ban) and this type stays trivially unit-testable.
/// </summary>
public sealed class CaboAReveliaNotice
{
    /// <summary>The 30-day cure window Decreto 12.068/2024 itself specifies for this notice type — not a tunable business parameter like the SLA/response deadlines elsewhere in this archetype family.</summary>
    public const int NoticeWindowDays = 30;

    public string PoleId { get; }
    public DateTimeOffset DetectedAt { get; }
    public CaboAReveliaStatus Status { get; private set; }
    public DateTimeOffset? NoticeIssuedAt { get; private set; }
    public DateTimeOffset? ResolvedAt { get; private set; }

    public CaboAReveliaNotice(string poleId, DateTimeOffset detectedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(poleId);
        PoleId = poleId;
        DetectedAt = detectedAt;
        Status = CaboAReveliaStatus.Detected;
    }

    public DateTimeOffset? NoticeDeadline => NoticeIssuedAt?.AddDays(NoticeWindowDays);

    /// <summary>Starts the 30-day clock. Only legal from <see cref="CaboAReveliaStatus.Detected"/>.</summary>
    public void IssueNotice(DateTimeOffset issuedAt)
    {
        if (Status != CaboAReveliaStatus.Detected)
            throw new InvalidOperationException($"Cannot issue a notice from status '{Status}'.");

        NoticeIssuedAt = issuedAt;
        Status = CaboAReveliaStatus.NoticeIssued;
    }

    /// <summary>
    /// Marks the cable as regularized or removed. Legal from <see cref="CaboAReveliaStatus.NoticeIssued"/>
    /// (resolved within the window) or <see cref="CaboAReveliaStatus.Expired"/> (resolved late,
    /// after escalation already happened — still worth recording as resolved).
    /// </summary>
    public void Resolve(DateTimeOffset resolvedAt)
    {
        if (Status is not (CaboAReveliaStatus.NoticeIssued or CaboAReveliaStatus.Expired))
            throw new InvalidOperationException($"Cannot resolve from status '{Status}'.");

        ResolvedAt = resolvedAt;
        Status = CaboAReveliaStatus.Resolved;
    }

    /// <summary>
    /// Call periodically (e.g. from a scheduled job) to advance a <see cref="CaboAReveliaStatus.NoticeIssued"/>
    /// notice to <see cref="CaboAReveliaStatus.Expired"/> once <paramref name="asOf"/> is past
    /// <see cref="NoticeDeadline"/>. A no-op in every other status.
    /// </summary>
    public void EvaluateExpiry(DateTimeOffset asOf)
    {
        if (Status != CaboAReveliaStatus.NoticeIssued) return;
        if (NoticeDeadline is { } deadline && asOf > deadline)
            Status = CaboAReveliaStatus.Expired;
    }
}
