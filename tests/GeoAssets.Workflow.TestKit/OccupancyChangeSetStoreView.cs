using GeoAssets.Core.Isp.NeutralOperator.Occupancy;

namespace GeoAssets.Workflow.TestKit;

/// <summary>Both ports of one change-set store, as one organization sees it.</summary>
public sealed record OccupancyChangeSetStoreView(IOccupancyChangeSetReader Reader, IOccupancyChangeSetWriter Writer);
