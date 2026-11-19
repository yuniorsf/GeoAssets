using GeoAssets.Core.Models.Geometry;
using GeoAssets.Workflow.Orders;

namespace GeoAssets.FieldService.FieldWork;

/// <summary>
/// Real implementation of <see cref="IFieldWorkTransitionGuard.CanArriveAsync"/> (XD01-221):
/// true when the crew's device-reported GPS position is within <see cref="ToleranceMeters"/> of
/// the order's target asset, via <see cref="IAssetProximityQuery.IsWithinDistanceAsync"/> — depends
/// only on that narrow port (XD01-386), never a concrete database, so this stays pure domain logic
/// whether the host answers it in process or over the core's API. Fails closed: when the port
/// cannot reach its asset source it throws <see cref="Core.Interfaces.GeoCoreUnavailableException"/>,
/// which this guard lets surface so the arrival transition is refused, never silently allowed.
///
/// <see cref="CanStartWorkAsync"/> is not this ticket's scope (XD01-222 owns the digital APR
/// safety-gate checklist) — always returns true here, same as
/// <see cref="NullFieldWorkTransitionGuard"/>, rather than composing a decorator for a check that
/// doesn't exist yet.
/// </summary>
public sealed class GeofenceArrivalTransitionGuard(IAssetProximityQuery proximityQuery) : IFieldWorkTransitionGuard
{
    public const double ToleranceMeters = 30;

    /// <summary>
    /// Uses the order's first associated <see cref="IServiceOrder.Features"/> entry as "the
    /// target asset" — the common case for a field-work order — falling back to the first persisted
    /// <see cref="ServiceOrder.FeatureIds"/> entry when the repository did not hydrate features (a
    /// host with no in-process asset source, such as FieldService.Server since XD01-386). An order
    /// with neither has nothing to confirm arrival at, so this returns false rather than vacuously
    /// allowing the transition.
    /// </summary>
    public Task<bool> CanArriveAsync(IServiceOrder order, GeoPoint devicePoint, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(devicePoint);

        var targetFeatureId = TargetFeatureId(order);
        return targetFeatureId is null
            ? Task.FromResult(false)
            : proximityQuery.IsWithinDistanceAsync(targetFeatureId, devicePoint, ToleranceMeters, ct);
    }

    private static string? TargetFeatureId(IServiceOrder order) => order switch
    {
        { Features.Count: > 0 } => order.Features[0].Id,
        ServiceOrder { FeatureIds: [var first, ..] } => first,
        _ => null,
    };

    public Task<bool> CanStartWorkAsync(IServiceOrder order, CancellationToken ct = default) => Task.FromResult(true);

    /// <summary>Not this ticket's scope (XD01-246 owns completion's photo-evidence gate) — always true, same as <see cref="NullFieldWorkTransitionGuard"/>.</summary>
    public Task<bool> CanCompleteAsync(IServiceOrder order, CancellationToken ct = default) => Task.FromResult(true);
}
