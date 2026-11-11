using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Services;
using GeoAssets.Workflow.Dispatch;

namespace GeoAssets.Workflow.Agents.Dispatch;

/// <summary>
/// Naive nearest-neighbor route builder — exists purely as the comparison baseline XD01-240's
/// AC measures <see cref="ClarkeWrightVrptwRoutingEngine"/> against ("a routed itinerary
/// measurably shorter than naive nearest-neighbor assignment on the same test set"). Not used
/// on the production dispatch path.
/// </summary>
public static class NearestNeighborBaselineRouter
{
    /// <summary>Starting from <paramref name="depotLocation"/>, repeatedly visits whichever
    /// remaining order is geodetically closest to the current position, then returns to depot.
    /// Returns the total round-trip distance in meters.</summary>
    public static double ComputeTotalDistanceMeters(
        GeoPoint depotLocation, IReadOnlyList<DispatchableOrder> orders)
    {
        ArgumentNullException.ThrowIfNull(depotLocation);
        ArgumentNullException.ThrowIfNull(orders);
        if (orders.Count == 0) return 0d;

        var remaining = new List<DispatchableOrder>(orders);
        var current = depotLocation;
        var total = 0d;

        while (remaining.Count > 0)
        {
            var nearestIndex = 0;
            var nearestDistance = GeodeticDistanceCalculator.DistanceInMeters(current, remaining[0].Location);

            for (var i = 1; i < remaining.Count; i++)
            {
                var distance = GeodeticDistanceCalculator.DistanceInMeters(current, remaining[i].Location);
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearestIndex = i;
                }
            }

            total += nearestDistance;
            current = remaining[nearestIndex].Location;
            remaining.RemoveAt(nearestIndex);
        }

        total += GeodeticDistanceCalculator.DistanceInMeters(current, depotLocation);
        return total;
    }
}
