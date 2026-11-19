namespace GeoAssets.Workflow.Targets;

/// <summary>
/// A feature a ledger reservation could not take because another order holds it with an exclusivity that blocks the
/// overlap (XD01-379). Conflict detection searches every organization; the holder's order is named only when it
/// belongs to the reserving organization — exactly what <c>/service-orders/targets/usages</c> already shows that
/// organization. For another organization's holder only the feature, the vertical and the exclusivity are reported.
/// </summary>
/// <param name="FeatureId">The contested feature.</param>
/// <param name="VerticalId">The vertical holding it.</param>
/// <param name="Exclusivity">The holder's exclusivity.</param>
/// <param name="OrderId">The holding order; null when it belongs to another organization.</param>
/// <param name="OrderTypeId">The holding order's type; null when it belongs to another organization.</param>
public sealed record TargetLedgerConflict(
    string FeatureId,
    string VerticalId,
    OrderTargetExclusivity Exclusivity,
    string? OrderId,
    string? OrderTypeId);
