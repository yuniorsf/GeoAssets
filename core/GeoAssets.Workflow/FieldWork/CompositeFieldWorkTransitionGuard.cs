using GeoAssets.Core.Models.Geometry;
using GeoAssets.Workflow.Orders;

namespace GeoAssets.Workflow.FieldWork;

/// <summary>
/// Composes the four single-purpose <see cref="IFieldWorkTransitionGuard"/> implementations —
/// <see cref="GeofenceArrivalTransitionGuard"/> (XD01-221), <see cref="SafetyChecklistTransitionGuard"/>
/// (XD01-222), <see cref="PhotoGatingTransitionGuard"/> (XD01-246), and
/// <see cref="TechnicianComplianceTransitionGuard"/> (XD01-253) — into the one guard a real
/// <see cref="FieldWorkOrderTransitioner"/> caller injects.
///
/// <see cref="CanStartWorkAsync"/> ANDs <paramref name="safetyGuard"/>,
/// <paramref name="photoGatingGuard"/>, and <paramref name="complianceGuard"/> — starting work
/// requires a passing safety checklist, (if the order carries a gated BoQ item) its Before-stage
/// photo evidence, and the assigned technician's certifications/equipment calibration not being
/// expired; a caller does not need to separately remember to ask all three.
/// <see cref="CanCompleteAsync"/> delegates only to <paramref name="photoGatingGuard"/>, the only
/// one of the four with an opinion on completion.
/// </summary>
public sealed class CompositeFieldWorkTransitionGuard(
    IFieldWorkTransitionGuard arrivalGuard,
    IFieldWorkTransitionGuard safetyGuard,
    IFieldWorkTransitionGuard photoGatingGuard,
    IFieldWorkTransitionGuard complianceGuard) : IFieldWorkTransitionGuard
{
    public Task<bool> CanArriveAsync(IServiceOrder order, GeoPoint devicePoint, CancellationToken ct = default) =>
        arrivalGuard.CanArriveAsync(order, devicePoint, ct);

    public async Task<bool> CanStartWorkAsync(IServiceOrder order, CancellationToken ct = default) =>
        await safetyGuard.CanStartWorkAsync(order, ct).ConfigureAwait(false)
        && await photoGatingGuard.CanStartWorkAsync(order, ct).ConfigureAwait(false)
        && await complianceGuard.CanStartWorkAsync(order, ct).ConfigureAwait(false);

    public Task<bool> CanCompleteAsync(IServiceOrder order, CancellationToken ct = default) =>
        photoGatingGuard.CanCompleteAsync(order, ct);
}
