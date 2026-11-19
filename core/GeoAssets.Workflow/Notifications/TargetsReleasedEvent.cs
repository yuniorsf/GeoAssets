namespace GeoAssets.Workflow.Notifications;

/// <summary>Targets of an order stopped holding their features (<c>geoassets.targets.targets-released.v1</c>): released through the ledger API, or because the owning vertical reported the order terminal.</summary>
public sealed record TargetsReleasedEvent : TargetLedgerEvent;
