using GeoAssets.Workflow.Targets;

namespace GeoAssets.Server.Targets;

/// <summary>
/// Response of <c>GET /api/targets/ledger/{vertical}/{orderId}/usages</c> (XD01-379): one page of the order's targets,
/// released ones included, with the <paramref name="Skip"/> and <paramref name="Take"/> that produced it and the
/// <paramref name="TotalCount"/> over all pages.
/// </summary>
public sealed record TargetLedgerUsagesResponse(IReadOnlyList<TargetLedgerEntry> Items, int Skip, int Take, int TotalCount);
