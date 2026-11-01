using GeoAssets.Core.Models.Geometry;
using GeoAssets.Workflow.Orders;

namespace GeoAssets.Workflow.FieldWork;

/// <summary>
/// Composes the two single-purpose <see cref="IFieldWorkTransitionGuard"/> implementations —
/// <see cref="GeofenceArrivalTransitionGuard"/> (XD01-221) and
/// <see cref="SafetyChecklistTransitionGuard"/> (XD01-222) — into the one guard a real
/// <see cref="FieldWorkOrderTransitioner"/> caller injects, each delegate only ever asked for the
/// one check it actually implements.
/// </summary>
public sealed class CompositeFieldWorkTransitionGuard(
    IFieldWorkTransitionGuard arrivalGuard,
    IFieldWorkTransitionGuard safetyGuard) : IFieldWorkTransitionGuard
{
    public Task<bool> CanArriveAsync(IServiceOrder order, GeoPoint devicePoint, CancellationToken ct = default) =>
        arrivalGuard.CanArriveAsync(order, devicePoint, ct);

    public Task<bool> CanStartWorkAsync(IServiceOrder order, CancellationToken ct = default) =>
        safetyGuard.CanStartWorkAsync(order, ct);
}
