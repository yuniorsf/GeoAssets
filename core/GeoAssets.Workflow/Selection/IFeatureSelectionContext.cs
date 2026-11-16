using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Providers;
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

    /// <summary>
    /// Cancellable area/attribute lookups for strategies that must not go through
    /// <see cref="Repository"/>'s synchronous, cache-backed spatial members. Defaults to an
    /// in-memory scan of <see cref="Repository"/>; a server host supplies its pushdown
    /// implementation instead (strategies are MEF-created, so this context is their only way to
    /// reach it).
    /// </summary>
    IAssetSpatialQuery SpatialQuery => new AssetProviderSpatialQuery(Repository);

    /// <summary>
    /// The organization the selection is made for. Strategies that use <see cref="SpatialQuery"/>
    /// forward it as <see cref="AssetSpatialFilter.OrganizationId"/>, so only that organization's
    /// features can be selected. <c>null</c> (the default) adds no organization predicate.
    /// </summary>
    Guid? OrganizationId => null;
}
