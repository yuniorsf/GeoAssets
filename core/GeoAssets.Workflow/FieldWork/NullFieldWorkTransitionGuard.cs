using GeoAssets.Workflow.Orders;

namespace GeoAssets.Workflow.FieldWork;

/// <summary>
/// Always allows both guarded transitions — the "no real guard wired yet" stand-in, same
/// convention as <see cref="NullOrderEventPublisher"/>, so a caller can adopt
/// <see cref="FieldWorkWorkflowGraph"/> before XD01-221/XD01-222 land without being forced to
/// implement anything.
/// </summary>
public sealed class NullFieldWorkTransitionGuard : IFieldWorkTransitionGuard
{
    public static readonly NullFieldWorkTransitionGuard Instance = new();

    public bool CanArrive(IServiceOrder order) => true;

    public bool CanStartWork(IServiceOrder order) => true;
}
