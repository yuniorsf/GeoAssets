using GeoAssets.Workflow.Orders;

namespace GeoAssets.Workflow.FieldWork;

/// <summary>
/// Pre-transition checks for the two guarded edges in <see cref="FieldWorkWorkflowGraph"/> —
/// structurally legal per the graph, but additionally conditioned on something the graph itself
/// can't express. A pure domain contract with no infrastructure dependency (no PostGIS, no HTTP,
/// no persistence): real implementations are separate, blocked tickets —
/// <see cref="CanArrive"/> needs a geofence check (<c>ST_DWithin</c>, XD01-221),
/// <see cref="CanStartWork"/> needs a signed digital APR safety checklist (XD01-222) — this
/// interface is the seam those tickets plug an implementation into.
/// </summary>
public interface IFieldWorkTransitionGuard
{
    /// <summary>Gates <see cref="FieldWorkStatus.EnRoute"/> → <see cref="FieldWorkStatus.OnSite"/>.</summary>
    bool CanArrive(IServiceOrder order);

    /// <summary>Gates <see cref="FieldWorkStatus.OnSite"/> → <see cref="FieldWorkStatus.InProgress"/>.</summary>
    bool CanStartWork(IServiceOrder order);
}
