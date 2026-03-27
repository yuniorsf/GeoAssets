using GeoAssets.Core.Models;

namespace GeoAssets.Workflow.Selection.Strategies;

/// <summary>
/// Selects all features that belong to a specific asset type.
/// Useful for orders that target a uniform class of infrastructure
/// (e.g. "all transformers", "all hydrants").
///
/// Required parameters:
///   assetTypeId  (string) — the asset type ID to filter by
/// </summary>
[ExportFeatureSelectionStrategy("asset-type-filter",
    Category    = "Filter",
    DisplayName = "Asset Type Filter",
    Description = "Selects all features of a specific asset type.")]
public sealed class AssetTypeFilterStrategy : IFeatureSelectionStrategy
{
    public string StrategyId  => "asset-type-filter";
    public string DisplayName => "Asset Type Filter";
    public string Description => "Selects all features of a specific asset type.";

    public Task<IReadOnlyList<GeoFeature>> SelectAsync(
        IFeatureSelectionContext context,
        CancellationToken ct = default)
    {
        var assetTypeId = (string)context.Parameters["assetTypeId"];
        var result      = context.Repository.GetByAssetType(assetTypeId);
        return Task.FromResult(result);
    }
}
