using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Workflow.Orders;

namespace GeoAssets.Workflow.FieldWork;

/// <summary>
/// Real implementation of <see cref="IFieldWorkTransitionGuard.CanArriveAsync"/> (XD01-221):
/// true when the crew's device-reported GPS position is within <see cref="ToleranceMeters"/> of
/// the order's target asset, via <see cref="IAssetProvider.IsWithinDistanceAsync"/> — depends
/// only on that abstraction, never a concrete database, so this stays pure domain logic even
/// though the active <see cref="IAssetProvider"/> may push the real geodesic check to PostGIS.
///
/// <see cref="CanStartWorkAsync"/> is not this ticket's scope (XD01-222 owns the digital APR
/// safety-gate checklist) — always returns true here, same as
/// <see cref="NullFieldWorkTransitionGuard"/>, rather than composing a decorator for a check that
/// doesn't exist yet.
/// </summary>
public sealed class GeofenceArrivalTransitionGuard(IAssetProvider assetProvider) : IFieldWorkTransitionGuard
{
    public const double ToleranceMeters = 30;

    /// <summary>
    /// Uses the order's first associated <see cref="IServiceOrder.Features"/> entry as "the
    /// target asset" — the common case for a field-work order. An order with no features has
    /// nothing to confirm arrival at, so this returns false rather than vacuously allowing the
    /// transition.
    /// </summary>
    public Task<bool> CanArriveAsync(IServiceOrder order, GeoPoint devicePoint, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(devicePoint);

        var targetFeatureId = order.Features.Count > 0 ? order.Features[0].Id : null;
        return targetFeatureId is null
            ? Task.FromResult(false)
            : assetProvider.IsWithinDistanceAsync(targetFeatureId, devicePoint, ToleranceMeters);
    }

    public Task<bool> CanStartWorkAsync(IServiceOrder order, CancellationToken ct = default) => Task.FromResult(true);

    /// <summary>Not this ticket's scope (XD01-246 owns completion's photo-evidence gate) — always true, same as <see cref="NullFieldWorkTransitionGuard"/>.</summary>
    public Task<bool> CanCompleteAsync(IServiceOrder order, CancellationToken ct = default) => Task.FromResult(true);
}
