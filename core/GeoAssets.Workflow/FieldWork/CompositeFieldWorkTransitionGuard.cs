using GeoAssets.Core.Models.Geometry;
using GeoAssets.Workflow.Orders;

namespace GeoAssets.Workflow.FieldWork;

/// <summary>
/// Composes the three single-purpose <see cref="IFieldWorkTransitionGuard"/> implementations —
/// <see cref="GeofenceArrivalTransitionGuard"/> (XD01-221), <see cref="SafetyChecklistTransitionGuard"/>
/// (XD01-222), and <see cref="PhotoGatingTransitionGuard"/> (XD01-246) — into the one guard a
/// real <see cref="FieldWorkOrderTransitioner"/> caller injects.
///
/// <see cref="CanStartWorkAsync"/> ANDs <paramref name="safetyGuard"/> with
/// <paramref name="photoGatingGuard"/> — starting work requires both a passing safety checklist
/// and (if the order carries a gated BoQ item) its Before-stage photo evidence; a caller does
/// not need to separately remember to ask both. <see cref="CanCompleteAsync"/> delegates only to
/// <paramref name="photoGatingGuard"/>, the only one of the three with an opinion on completion.
/// </summary>
public sealed class CompositeFieldWorkTransitionGuard(
    IFieldWorkTransitionGuard arrivalGuard,
    IFieldWorkTransitionGuard safetyGuard,
    IFieldWorkTransitionGuard photoGatingGuard) : IFieldWorkTransitionGuard
{
    public Task<bool> CanArriveAsync(IServiceOrder order, GeoPoint devicePoint, CancellationToken ct = default) =>
        arrivalGuard.CanArriveAsync(order, devicePoint, ct);

    public async Task<bool> CanStartWorkAsync(IServiceOrder order, CancellationToken ct = default) =>
        await safetyGuard.CanStartWorkAsync(order, ct).ConfigureAwait(false)
        && await photoGatingGuard.CanStartWorkAsync(order, ct).ConfigureAwait(false);

    public Task<bool> CanCompleteAsync(IServiceOrder order, CancellationToken ct = default) =>
        photoGatingGuard.CanCompleteAsync(order, ct);
}
