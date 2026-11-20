using GeoAssets.Workflow.Targets;

namespace GeoAssets.Workflow.Rest;

/// <summary>The kernel's <c>GET /api/targets/ledger/{vertical}/{orderId}/usages</c> body (XD01-379).</summary>
internal sealed record TargetLedgerUsagesEnvelope(IReadOnlyList<TargetLedgerEntry> Items, int Skip, int Take, int TotalCount);
