using GeoAssets.Core.Models.Geometry;
using GeoAssets.Workflow.Orders;

namespace GeoAssets.Workflow.FieldWork;

/// <summary>
/// Pre-transition checks for the three guarded edges in <see cref="FieldWorkWorkflowGraph"/> —
/// structurally legal per the graph, but additionally conditioned on something the graph itself
/// can't express. A pure domain contract with no infrastructure dependency of its own (an
/// implementation may depend on one, e.g. <see cref="GeofenceArrivalTransitionGuard"/> depends
/// only on the <see cref="GeoAssets.Core.Interfaces.IAssetProvider"/> abstraction, never a
/// concrete database): <see cref="CanArriveAsync"/> needs a geofence check (<c>ST_DWithin</c>,
/// XD01-221, implemented), <see cref="CanStartWorkAsync"/> needs a signed digital APR safety
/// checklist (XD01-222, implemented) and Before-stage photo evidence for any gated BoQ item
/// (XD01-246, implemented), <see cref="CanCompleteAsync"/> needs all required photo-evidence
/// stages for every gated BoQ item (XD01-246). All three methods are async since any may need to
/// check a backing store.
/// </summary>
public interface IFieldWorkTransitionGuard
{
    /// <summary>
    /// Gates <see cref="FieldWorkStatus.EnRoute"/> → <see cref="FieldWorkStatus.OnSite"/>.
    /// <paramref name="devicePoint"/> is the crew's device-reported GPS position at the moment
    /// of the Arrive action — not something the order itself carries, since it's a per-attempt
    /// input, not stored state.
    /// </summary>
    Task<bool> CanArriveAsync(IServiceOrder order, GeoPoint devicePoint, CancellationToken ct = default);

    /// <summary>Gates <see cref="FieldWorkStatus.OnSite"/> → <see cref="FieldWorkStatus.InProgress"/>.</summary>
    Task<bool> CanStartWorkAsync(IServiceOrder order, CancellationToken ct = default);

    /// <summary>Gates <see cref="FieldWorkStatus.InProgress"/> → <see cref="FieldWorkStatus.Completed"/>.</summary>
    Task<bool> CanCompleteAsync(IServiceOrder order, CancellationToken ct = default);
}
