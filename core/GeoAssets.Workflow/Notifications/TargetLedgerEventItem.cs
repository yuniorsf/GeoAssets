namespace GeoAssets.Workflow.Notifications;

/// <summary>One target named by a <see cref="TargetLedgerEvent"/>.</summary>
/// <param name="TargetId">The target's ledger id.</param>
/// <param name="FeatureId">The feature it holds.</param>
public sealed record TargetLedgerEventItem(string TargetId, string FeatureId);
