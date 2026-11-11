using GeoAssets.Core.Models.Geometry;

namespace GeoAssets.Workflow.Dispatch;

/// <summary>
/// Routable projection of an open <see cref="Orders.ServiceOrder"/> — just enough for
/// <see cref="ICrewRoutingEngine"/> to cluster/route/match it, independent of the full order
/// aggregate (XD01-240).
/// </summary>
public sealed record DispatchableOrder(
    string OrderId,
    GeoPoint Location,
    IReadOnlySet<string> RequiredSkills,
    IReadOnlySet<string> RequiredEquipment,
    TimeSpan ServiceDuration,
    DateTimeOffset? TimeWindowEnd = null);
