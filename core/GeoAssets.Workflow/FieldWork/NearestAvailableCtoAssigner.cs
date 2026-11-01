using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Services;

namespace GeoAssets.Workflow.FieldWork;

/// <summary>
/// Assigns a new FTTH drop to the nearest CTO that still has a free splitter port (XD01-273's
/// AC: "Nearest-available-CTO assignment is unit-tested against a small synthetic CTO/port
/// fixture"). Pure domain logic over a caller-supplied <see cref="CtoPortAvailability"/>
/// snapshot — no <c>IAssetProvider</c> dependency of its own, same division of responsibility as
/// <see cref="WaterPavingSubOrderSpawner"/>.
/// </summary>
public static class NearestAvailableCtoAssigner
{
    /// <summary>
    /// The nearest <paramref name="candidates"/> entry with <see cref="CtoPortAvailability.HasAvailablePort"/>
    /// true, by real-world (<see cref="GeodeticDistanceCalculator"/>) distance from
    /// <paramref name="dropLocation"/>. Null when no candidate has a free port.
    /// </summary>
    public static CtoPortAvailability? FindNearest(GeoPoint dropLocation, IEnumerable<CtoPortAvailability> candidates)
    {
        ArgumentNullException.ThrowIfNull(dropLocation);
        ArgumentNullException.ThrowIfNull(candidates);

        return candidates
            .Where(c => c.HasAvailablePort)
            .OrderBy(c => GeodeticDistanceCalculator.DistanceInMeters(dropLocation, c.Location))
            .FirstOrDefault();
    }
}
