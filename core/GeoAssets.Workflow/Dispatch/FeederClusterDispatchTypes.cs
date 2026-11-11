namespace GeoAssets.Workflow.Dispatch;

/// <summary>
/// Well-known constants for the feeder/pressure-zone clustering + VRPTW crew-dispatch path
/// (XD01-240), following the same "plain string constant, no central registry" convention as
/// <c>GeoAssets.Workflow.Scada.ScadaOutageOrderTypes</c> (XD01-239).
/// </summary>
public static class FeederClusterDispatchTypes
{
    /// <summary><see cref="Core.Models.GeoFeatureProperties.AssetTypeId"/> for a feeder/
    /// pressure-zone source node (an electrical feeder breaker or a water pressure-zone
    /// inlet) — the same upstream role <c>feeds</c>-kind <see cref="Core.Models.TopoEdge"/>s
    /// already model for <c>ScadaOutageOrderTypes.FeedsEdgeKind</c>. Orders cluster by their
    /// nearest ancestor feature carrying this type.</summary>
    public const string FeederAssetTypeId = "feeder";

    /// <summary><see cref="Core.Models.GeoFeatureProperties.AssetTypeId"/> for a field crew
    /// unit eligible for feeder-clustered dispatch, distinct from
    /// <c>ScadaOutageOrderTypes.CrewAssetTypeId</c> (emergency-only) — this path dispatches
    /// routine open trouble calls, not SCADA breaker-trip emergencies.</summary>
    public const string RoutineCrewAssetTypeId = "crew";

    /// <summary><see cref="Core.Models.GeoFeatureProperties.CustomAttributes"/> key on a crew
    /// feature: a comma-separated list of skill tags the crew is qualified for (e.g.
    /// <c>"electrical,confined-space"</c>).</summary>
    public const string CrewSkillsAttributeKey = "skills";

    /// <summary><see cref="Core.Models.GeoFeatureProperties.CustomAttributes"/> key on a crew
    /// feature: a comma-separated list of equipment the crew carries (e.g.
    /// <c>"bucket-truck,hot-stick"</c>).</summary>
    public const string CrewEquipmentAttributeKey = "equipment";

    /// <summary><see cref="Orders.ServiceOrder.Attributes"/> key: a comma-separated list of
    /// skill tags an order requires a crew to document before dispatch.</summary>
    public const string OrderRequiredSkillsAttributeKey = "requiredSkills";

    /// <summary><see cref="Orders.ServiceOrder.Attributes"/> key: a comma-separated list of
    /// equipment tags an order requires a crew to document before dispatch.</summary>
    public const string OrderRequiredEquipmentAttributeKey = "requiredEquipment";

    /// <summary>Default on-site service duration assumed for an order that doesn't document
    /// its own — a single, well-known fallback rather than inventing per-order estimates.</summary>
    public static readonly TimeSpan DefaultServiceDuration = TimeSpan.FromMinutes(45);
}
