using GeoAssets.Core.Models.Geometry;

namespace GeoAssets.Core.Isp;

/// <summary>One <see cref="IspAssetTypes.Cto"/> whose occupancy is at or above a heatmap query's threshold (XD01-281).</summary>
public sealed record CtoOccupancyHeatmapEntry(
    string CtoFeatureId,
    GeoPoint Location,
    int OccupiedPorts,
    int TotalPorts,
    double OccupiedPercentage);
