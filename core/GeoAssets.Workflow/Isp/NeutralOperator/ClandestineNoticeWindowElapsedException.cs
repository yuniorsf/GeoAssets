namespace GeoAssets.Workflow.Isp.NeutralOperator;

/// <summary>
/// Thrown by <see cref="IClandestineAttachmentService.RegularizeAsync"/> when the statutory
/// 30-day window has already elapsed. XD01-263's acceptance criteria only specify the
/// within-window success path; the safer reading was chosen for the unstated elapsed-window
/// case — blocking the normal regularization transition rather than silently honoring a late
/// self-service correction, since by that point the attachment is meant to proceed through the
/// cleanup-drive batching workflow (XD01-264) instead, not a quiet after-the-fact regularization
/// that would undermine the statutory notice's enforcement purpose.
/// </summary>
public sealed class ClandestineNoticeWindowElapsedException : InvalidOperationException
{
    public string ServiceOrderId { get; } = string.Empty;
    public DateTimeOffset DeadlineUtc { get; }
    public DateTimeOffset AttemptedAtUtc { get; }

    public ClandestineNoticeWindowElapsedException(string serviceOrderId, DateTimeOffset deadlineUtc, DateTimeOffset attemptedAtUtc)
        : base(
            $"ServiceOrder '{serviceOrderId}' missed its regularization deadline ({deadlineUtc:O}); " +
            $"regularization was attempted at {attemptedAtUtc:O}. Route to the cleanup-drive process instead.")
    {
        ServiceOrderId = serviceOrderId;
        DeadlineUtc = deadlineUtc;
        AttemptedAtUtc = attemptedAtUtc;
    }

    public ClandestineNoticeWindowElapsedException()
    {
    }

    public ClandestineNoticeWindowElapsedException(string message) : base(message)
    {
    }

    public ClandestineNoticeWindowElapsedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
