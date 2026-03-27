using GeoAssets.Core.Interfaces;
using GeoAssets.Workflow.Orders;

namespace GeoAssets.Workflow.Selection;

/// <summary>Default concrete implementation of <see cref="IFeatureSelectionContext"/>.</summary>
public sealed class FeatureSelectionContext : IFeatureSelectionContext
{
    public IAssetProvider             Repository       { get; init; } = null!;
    public IServiceOrder?               TargetOrder      { get; init; }
    public IServiceOrderRepository?     OrderRepository  { get; init; }
    public IReadOnlyDictionary<string, object> Parameters { get; init; } =
        new Dictionary<string, object>();
}
