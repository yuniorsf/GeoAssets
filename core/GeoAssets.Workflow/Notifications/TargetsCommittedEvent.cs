namespace GeoAssets.Workflow.Notifications;

/// <summary>Targets of an order were committed — their work is done (<c>geoassets.targets.targets-committed.v1</c>).</summary>
public sealed record TargetsCommittedEvent : TargetLedgerEvent;
