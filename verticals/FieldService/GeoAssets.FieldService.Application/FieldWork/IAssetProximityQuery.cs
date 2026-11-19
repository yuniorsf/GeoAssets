using GeoAssets.Core.Models.Geometry;

namespace GeoAssets.FieldService.FieldWork;

/// <summary>
/// The one asset read <see cref="GeofenceArrivalTransitionGuard"/> needs (XD01-386): is a point
/// within a real-world distance of a feature. A narrow port rather than the whole
/// <c>IAssetProvider</c>, so a vertical host answers it over the core's API instead of opening
/// the core asset database (Invariant 1, Core Database Encapsulation).
/// </summary>
public interface IAssetProximityQuery
{
    /// <summary>
    /// True when <paramref name="point"/> is within <paramref name="toleranceMeters"/> of the
    /// feature identified by <paramref name="featureId"/>; false when the feature doesn't exist or
    /// has no geometry. An implementation that cannot reach its asset source throws
    /// <see cref="Core.Interfaces.GeoCoreUnavailableException"/> rather than answering false or true.
    /// </summary>
    Task<bool> IsWithinDistanceAsync(string featureId, GeoPoint point, double toleranceMeters, CancellationToken ct);
}
