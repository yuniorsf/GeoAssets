namespace GeoAssets.Workflow.Targets;

/// <summary>
/// A ledger reservation would take features that another order holds with blocking exclusivity (XD01-379); nothing was
/// written. <see cref="Conflicts"/> lists each contested feature's holders, as <see cref="TargetLedgerConflict"/>
/// redacts them.
/// </summary>
public sealed class TargetLedgerConflictException : InvalidOperationException
{
    public TargetLedgerConflictException()
        : this([])
    {
    }

    public TargetLedgerConflictException(string message)
        : base(message)
    {
        Conflicts = [];
    }

    public TargetLedgerConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
        Conflicts = [];
    }

    public TargetLedgerConflictException(IReadOnlyList<TargetLedgerConflict> conflicts)
        : base($"{conflicts?.Count ?? 0} target(s) are held by another order with blocking exclusivity.")
    {
        ArgumentNullException.ThrowIfNull(conflicts);
        Conflicts = conflicts;
    }

    /// <summary>The contested features and their holders.</summary>
    public IReadOnlyList<TargetLedgerConflict> Conflicts { get; }
}
