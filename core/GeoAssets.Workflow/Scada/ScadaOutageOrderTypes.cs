namespace GeoAssets.Workflow.Scada;

/// <summary>
/// Well-known constants for the SCADA/OMS breaker-trip auto-dispatch path (XD01-239).
/// </summary>
public static class ScadaOutageOrderTypes
{
    /// <summary><see cref="Orders.ServiceOrder.OrderTypeId"/> for an auto-created emergency
    /// outage order. Uses the global default 12-state workflow graph — no custom
    /// <see cref="Orders.OrderType.States"/>/<see cref="Orders.OrderType.Transitions"/> are
    /// needed for this ticket's AC.</summary>
    public const string EmergencyOutageDispatchId = "emergency-outage-dispatch";

    /// <summary><see cref="Core.Models.TopoEdge.Kind"/> for electrical feeder/downstream-supply
    /// edges, matching the constant already established in
    /// <c>GeoAssets.IpBilling.Resolution.HttpPoleResolver.FeedsEdgeKind</c>.</summary>
    public const string FeedsEdgeKind = "feeds";

    /// <summary><see cref="Core.Models.GeoFeatureProperties.AssetTypeId"/> for a field crew
    /// unit — a plain, well-known string, same convention as other domain-specific asset
    /// types in this codebase (no central <c>AssetType</c> registry entry needed).</summary>
    public const string CrewAssetTypeId = "scada-crew";

    /// <summary><see cref="Core.Models.GeoFeatureProperties.CustomAttributes"/> key marking a
    /// crew feature as qualified for emergency outage dispatch (value <c>"true"</c>) — a plain
    /// well-known key, same "no dedicated column" convention as <c>scopeId</c>.</summary>
    public const string EmergencyQualifiedAttributeKey = "emergencyQualified";

    /// <summary><see cref="Core.Models.GeoFeatureProperties.CustomAttributes"/> key flagging a
    /// downstream facility as critical (e.g. a healthcare facility) — non-empty value is the
    /// facility type/reason, e.g. <c>"Healthcare"</c>.</summary>
    public const string CriticalFacilityAttributeKey = "criticalFacilityType";

    /// <summary><see cref="Orders.ServiceOrder.Attributes"/> key recording the SCADA-reported
    /// tripped switch's feature ID that triggered this order.</summary>
    public const string TrippedSwitchAttributeKey = "trippedSwitchFeatureId";

    /// <summary><see cref="Orders.ServiceOrder.Attributes"/> key recording the count of
    /// critical-facility features found downstream of the trip, for quick triage without
    /// re-walking <see cref="Orders.ServiceOrder.Features"/>.</summary>
    public const string CriticalFacilityCountAttributeKey = "criticalFacilityCount";

    /// <summary>Default search radius (degrees) used to locate a nearby qualified crew.
    /// ~0.5° is roughly 50km at the equator — generous for an emergency dispatch search without
    /// scanning the entire asset collection; see <see cref="Core.Interfaces.IAssetProvider.GetNearby"/>.</summary>
    public const double CrewSearchRadiusDegrees = 0.5;
}
