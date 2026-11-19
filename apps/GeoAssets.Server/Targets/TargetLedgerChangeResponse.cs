using GeoAssets.Workflow.Notifications;

namespace GeoAssets.Server.Targets;

/// <summary>
/// Response of the ledger's release and commit routes (XD01-379): the targets this call changed — empty when there was
/// nothing left to change, which is what a repeated call returns.
/// </summary>
public sealed record TargetLedgerChangeResponse(string VerticalId, string OrderId, int Changed, IReadOnlyList<TargetLedgerEventItem> Targets);
