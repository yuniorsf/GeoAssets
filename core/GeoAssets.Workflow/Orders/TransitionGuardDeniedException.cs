namespace GeoAssets.Workflow.Orders;

/// <summary>A transition an <see cref="ITransitionGuard"/> denied (XD01-362); carries the guard's typed verdict.</summary>
public sealed class TransitionGuardDeniedException : Exception
{
    public TransitionGuardDeniedException()
        : this(GuardVerdict.Deny("workflow.transition-denied", "The transition was denied."), string.Empty, string.Empty)
    {
    }

    public TransitionGuardDeniedException(string message)
        : base(message) => Verdict = GuardVerdict.Deny("workflow.transition-denied", message);

    public TransitionGuardDeniedException(string message, Exception innerException)
        : base(message, innerException) => Verdict = GuardVerdict.Deny("workflow.transition-denied", message);

    public TransitionGuardDeniedException(GuardVerdict verdict, string from, string to)
        : base(verdict?.Reason ?? throw new ArgumentNullException(nameof(verdict)))
    {
        Verdict = verdict;
        From = from;
        To = to;
    }

    public GuardVerdict Verdict { get; }

    public string? From { get; }

    public string? To { get; }
}
