using GeoAssets.Workflow.Dispatch;

namespace GeoAssets.Workflow.Agents.Dispatch;

/// <summary>Summary of one <see cref="FeederClusterAutoDispatchService.DispatchOpenOrdersAsync"/>
/// pass (XD01-240).</summary>
public sealed record FeederClusterDispatchRunResult(
    IReadOnlyList<CrewItinerary> Itineraries,
    IReadOnlyList<string> DispatchedOrderIds,
    IReadOnlyList<string> UnassignedOrderIds,
    IReadOnlyList<string> SkippedNoLocationOrderIds);
