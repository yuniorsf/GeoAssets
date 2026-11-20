using GeoAssets.Workflow.Notifications;

namespace GeoAssets.Workflow.Rest;

/// <summary>The kernel's release and commit body (XD01-379): the targets the call changed.</summary>
internal sealed record TargetLedgerChangeEnvelope(string VerticalId, string OrderId, int Changed, IReadOnlyList<TargetLedgerEventItem> Targets);
