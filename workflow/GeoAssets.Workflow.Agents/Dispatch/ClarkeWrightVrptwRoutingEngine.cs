using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Services;
using GeoAssets.Workflow.Dispatch;

namespace GeoAssets.Workflow.Agents.Dispatch;

/// <summary>
/// VRPTW routing via the Clarke-Wright parallel savings construction heuristic (XD01-240) —
/// NOT an exact/optimal solver. Starts every order as its own single-stop route, then greedily
/// merges the pair of route endpoints with the highest "savings" (<c>d(depot,i) + d(depot,j) -
/// d(i,j)</c>) whenever the merge doesn't break a stop's <see cref="DispatchableOrder.TimeWindowEnd"/>,
/// until no more route-endpoint merges remain feasible. Any leftover disjoint fragments (which
/// time-window infeasibility can leave behind) are then joined end-to-end by nearest endpoint,
/// so a single crew still gets one complete sequence covering every assigned order.
/// </summary>
public sealed class ClarkeWrightVrptwRoutingEngine : ICrewRoutingEngine
{
    /// <summary>Assumed average travel speed used only to convert distance into an estimated
    /// arrival clock time for <see cref="CrewItineraryStop.EstimatedArrival"/> and time-window
    /// feasibility checks — a deliberately simple constant, not a real routing-time model.</summary>
    private const double AssumedTravelSpeedMetersPerSecond = 11.11; // ~40 km/h

    public CrewItinerary BuildItinerary(
        string crewFeatureId, GeoPoint depotLocation, IReadOnlyList<DispatchableOrder> orders, DateTimeOffset shiftStart)
    {
        ArgumentException.ThrowIfNullOrEmpty(crewFeatureId);
        ArgumentNullException.ThrowIfNull(depotLocation);
        ArgumentNullException.ThrowIfNull(orders);

        if (orders.Count == 0)
            return new CrewItinerary(crewFeatureId, [], 0d);

        if (orders.Count == 1)
            return BuildItineraryFromSequence(crewFeatureId, depotLocation, [orders[0]], shiftStart);

        var distinctOrders = orders.Distinct().ToList();
        var routeOf = new Dictionary<DispatchableOrder, List<DispatchableOrder>>();
        foreach (var order in distinctOrders)
            routeOf[order] = [order];

        var savings = new List<(DispatchableOrder I, DispatchableOrder J, double Saving)>();
        for (var i = 0; i < distinctOrders.Count; i++)
            for (var j = i + 1; j < distinctOrders.Count; j++)
            {
                var a = distinctOrders[i];
                var b = distinctOrders[j];
                var saving = GeodeticDistanceCalculator.DistanceInMeters(depotLocation, a.Location)
                           + GeodeticDistanceCalculator.DistanceInMeters(depotLocation, b.Location)
                           - GeodeticDistanceCalculator.DistanceInMeters(a.Location, b.Location);
                savings.Add((a, b, saving));
            }
        savings.Sort((x, y) => y.Saving.CompareTo(x.Saving));

        foreach (var (i, j, _) in savings)
        {
            var routeI = routeOf[i];
            var routeJ = routeOf[j];
            if (ReferenceEquals(routeI, routeJ)) continue;

            List<DispatchableOrder>? merged = null;
            if (ReferenceEquals(routeI[^1], i) && ReferenceEquals(routeJ[0], j))
                merged = [.. routeI, .. routeJ];
            else if (ReferenceEquals(routeJ[^1], j) && ReferenceEquals(routeI[0], i))
                merged = [.. routeJ, .. routeI];

            if (merged is null || !IsTimeWindowFeasible(depotLocation, merged, shiftStart))
                continue;

            foreach (var order in merged)
                routeOf[order] = merged;
        }

        var fragments = new List<List<DispatchableOrder>>();
        var seen = new HashSet<List<DispatchableOrder>>();
        foreach (var order in distinctOrders)
            if (seen.Add(routeOf[order]))
                fragments.Add(routeOf[order]);

        var finalSequence = JoinFragments(depotLocation, fragments);
        return BuildItineraryFromSequence(crewFeatureId, depotLocation, finalSequence, shiftStart);
    }

    private static bool IsTimeWindowFeasible(
        GeoPoint depotLocation, List<DispatchableOrder> sequence, DateTimeOffset shiftStart)
    {
        var current = depotLocation;
        var clock = shiftStart;
        foreach (var order in sequence)
        {
            var travelSeconds = GeodeticDistanceCalculator.DistanceInMeters(current, order.Location) / AssumedTravelSpeedMetersPerSecond;
            clock = clock.AddSeconds(travelSeconds);
            if (order.TimeWindowEnd is { } deadline && clock > deadline)
                return false;

            clock = clock.Add(order.ServiceDuration);
            current = order.Location;
        }
        return true;
    }

    private static List<DispatchableOrder> JoinFragments(
        GeoPoint depotLocation, List<List<DispatchableOrder>> fragments)
    {
        var remaining = new List<List<DispatchableOrder>>(fragments);
        var result = new List<DispatchableOrder>();
        var current = depotLocation;

        while (remaining.Count > 0)
        {
            var bestFragmentIndex = 0;
            var bestReversed = false;
            var bestDistance = double.MaxValue;

            for (var k = 0; k < remaining.Count; k++)
            {
                var fragment = remaining[k];
                var distanceToStart = GeodeticDistanceCalculator.DistanceInMeters(current, fragment[0].Location);
                var distanceToEnd = GeodeticDistanceCalculator.DistanceInMeters(current, fragment[^1].Location);

                if (distanceToStart < bestDistance)
                {
                    bestDistance = distanceToStart;
                    bestFragmentIndex = k;
                    bestReversed = false;
                }
                if (distanceToEnd < bestDistance)
                {
                    bestDistance = distanceToEnd;
                    bestFragmentIndex = k;
                    bestReversed = true;
                }
            }

            var chosen = remaining[bestFragmentIndex];
            remaining.RemoveAt(bestFragmentIndex);
            if (bestReversed)
                chosen = [.. Enumerable.Reverse(chosen)];

            result.AddRange(chosen);
            current = chosen[^1].Location;
        }

        return result;
    }

    private static CrewItinerary BuildItineraryFromSequence(
        string crewFeatureId, GeoPoint depotLocation, List<DispatchableOrder> sequence, DateTimeOffset shiftStart)
    {
        var stops = new List<CrewItineraryStop>(sequence.Count);
        var current = depotLocation;
        var clock = shiftStart;
        var totalDistance = 0d;

        for (var i = 0; i < sequence.Count; i++)
        {
            var order = sequence[i];
            var legDistance = GeodeticDistanceCalculator.DistanceInMeters(current, order.Location);
            totalDistance += legDistance;
            clock = clock.AddSeconds(legDistance / AssumedTravelSpeedMetersPerSecond);
            stops.Add(new CrewItineraryStop(order.OrderId, i, clock));
            clock = clock.Add(order.ServiceDuration);
            current = order.Location;
        }

        totalDistance += GeodeticDistanceCalculator.DistanceInMeters(current, depotLocation);
        return new CrewItinerary(crewFeatureId, stops, totalDistance);
    }
}
