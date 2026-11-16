using GeoAssets.Core.Isp.NeutralOperator.Occupancy;

namespace GeoAssets.Workflow.TestKit;

/// <summary>Both ports of one occupancy store, as one organization sees it.</summary>
public sealed record PoleOccupancyStoreView(IPoleOccupancyReader Reader, IPoleOccupancyWriter Writer);
