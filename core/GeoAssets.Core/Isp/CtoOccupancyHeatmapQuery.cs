using GeoAssets.Core.Models;

namespace GeoAssets.Core.Isp;

/// <summary>
/// Highlights exhausted distribution zones (XD01-281's AC) — each <see cref="IspAssetTypes.Cto"/>
/// at or above a configurable occupancy threshold, feeding network-expansion planning use cases.
/// A CTO without a captured location (no <see cref="GeoFeature.Geometry"/> yet) is skipped —
/// there is no point to plot it against.
/// </summary>
public static class CtoOccupancyHeatmapQuery
{
    public static IReadOnlyList<CtoOccupancyHeatmapEntry> GetAboveThreshold(
        IEnumerable<GeoFeature> ctoFeatures, double thresholdPercentage)
    {
        ArgumentNullException.ThrowIfNull(ctoFeatures);

        var result = new List<CtoOccupancyHeatmapEntry>();
        foreach (var feature in ctoFeatures)
        {
            if (feature.Geometry is null)
                continue;

            var (occupied, total) = CtoCapacityCalculator.GetOccupiedAndTotal(feature.Properties.CustomAttributes);
            var percentage = CtoCapacityCalculator.OccupiedPortPercentage(feature.Properties.CustomAttributes);
            if (percentage < thresholdPercentage)
                continue;

            result.Add(new CtoOccupancyHeatmapEntry(feature.Id, feature.Geometry.Centroid, occupied, total, percentage));
        }

        return result;
    }
}
