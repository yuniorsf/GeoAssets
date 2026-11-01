using GeoAssets.Core.Models.Geometry;
using GeoAssets.Workflow.Orders;

namespace GeoAssets.Workflow.FieldWork;

/// <summary>
/// Always allows both guarded transitions — the "no real guard wired yet" stand-in, same
/// convention as <see cref="NullOrderEventPublisher"/>, so a caller can adopt
/// <see cref="FieldWorkWorkflowGraph"/> before XD01-222 (the digital APR safety-gate checklist
/// <see cref="CanStartWorkAsync"/> needs) lands without being forced to implement anything.
/// </summary>
public sealed class NullFieldWorkTransitionGuard : IFieldWorkTransitionGuard
{
    public static readonly NullFieldWorkTransitionGuard Instance = new();

    public Task<bool> CanArriveAsync(IServiceOrder order, GeoPoint devicePoint, CancellationToken ct = default) => Task.FromResult(true);

    public Task<bool> CanStartWorkAsync(IServiceOrder order, CancellationToken ct = default) => Task.FromResult(true);

    public Task<bool> CanCompleteAsync(IServiceOrder order, CancellationToken ct = default) => Task.FromResult(true);
}
