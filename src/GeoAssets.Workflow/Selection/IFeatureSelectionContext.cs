using GeoAssets.Core.Interfaces;
using GeoAssets.Workflow.Orders;

namespace GeoAssets.Workflow.Selection;

/// <summary>
/// Runtime context passed to every <see cref="IFeatureSelectionStrategy.SelectAsync"/> call.
/// Provides access to the asset repository and any strategy-specific parameters.
/// </summary>
public interface IFeatureSelectionContext
{
    /// <summary>The full asset repository — strategies query it to resolve features.</summary>
    IAssetProvider Repository { get; }

    /// <summary>
    /// The order being populated. Useful for strategies that inspect
    /// the order's parent/children (e.g. <c>inherit-parent</c>).
    /// </summary>
    IServiceOrder? TargetOrder { get; }

    /// <summary>
    /// The order repository — needed when a strategy must navigate the order hierarchy
    /// (e.g. fetching features from a sibling or ancestor order).
    /// </summary>
    IServiceOrderRepository? OrderRepository { get; }

    /// <summary>Strategy-specific parameters (e.g. bounding box coords, feature IDs list).</summary>
    IReadOnlyDictionary<string, object> Parameters { get; }
}
