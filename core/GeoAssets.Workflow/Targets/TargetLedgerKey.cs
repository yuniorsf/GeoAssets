namespace GeoAssets.Workflow.Targets;

/// <summary>
/// The ledger's key of one order (XD01-379): the vertical that owns it and its id within that vertical. Two verticals'
/// orders that share an id are different orders.
/// </summary>
/// <param name="VerticalId">The owning vertical; <see cref="TargetLedger.KernelVerticalId"/> for an in-process order.</param>
/// <param name="OrderId">The order's id within that vertical.</param>
public sealed record TargetLedgerKey(string VerticalId, string OrderId);
