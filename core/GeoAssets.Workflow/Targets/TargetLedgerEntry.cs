namespace GeoAssets.Workflow.Targets;

/// <summary>One target of a ledger order, with when it was released (null while it still holds its feature).</summary>
/// <param name="Target">The target.</param>
/// <param name="ReleasedAt">UTC instant the target was released, or null while it is open.</param>
public sealed record TargetLedgerEntry(ServiceOrderTarget Target, DateTime? ReleasedAt);
