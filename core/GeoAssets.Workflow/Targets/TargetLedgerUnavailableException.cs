namespace GeoAssets.Workflow.Targets;

/// <summary>
/// Thrown by a vertical host's target-ledger client (XD01-382) when the kernel's global target ledger cannot serve the
/// call now: the kernel is unreachable or failed, or it does not know the vertical's order type yet because the
/// vertical's descriptor registration is still pending. Nothing was reserved; the caller may retry.
/// </summary>
public sealed class TargetLedgerUnavailableException : InvalidOperationException
{
    public TargetLedgerUnavailableException()
    {
    }

    public TargetLedgerUnavailableException(string message) : base(message)
    {
    }

    public TargetLedgerUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
