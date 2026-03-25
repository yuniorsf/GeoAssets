using GeoAssets.Core.Models;

namespace GeoAssets.Workflow.Selection;

/// <summary>
/// Encapsulates a single way of producing a set of <see cref="GeoFeature"/> for a service order.
///
/// Each concrete strategy represents a different user or system interaction model:
///   - drawing a bounding box on the map
///   - picking features one by one
///   - inheriting features from a parent / child order
///   - applying an asset-type filter
///   - running a background/automated process
///   - topology traversal (upstream/downstream)
///   … and any custom strategy provided by external plugins.
///
/// Strategies are discovered by MEF via <see cref="ExportFeatureSelectionStrategyAttribute"/>.
/// </summary>
public interface IFeatureSelectionStrategy
{
    /// <summary>Stable, unique identifier (e.g. "bounding-box", "manual", "inherit-parent").</summary>
    string StrategyId { get; }

    /// <summary>Human-readable name shown in the UI.</summary>
    string DisplayName { get; }

    /// <summary>Short description of how the strategy works.</summary>
    string Description { get; }

    /// <summary>
    /// Executes the selection logic and returns the resolved feature set.
    /// All strategy-specific inputs come through <see cref="IFeatureSelectionContext.Parameters"/>.
    /// </summary>
    Task<IReadOnlyList<GeoFeature>> SelectAsync(
        IFeatureSelectionContext context,
        CancellationToken ct = default);
}
