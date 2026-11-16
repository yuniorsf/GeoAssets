using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Providers;
using GeoAssets.Workflow.Orders;

namespace GeoAssets.Workflow.Selection;

/// <summary>Default concrete implementation of <see cref="IFeatureSelectionContext"/>.</summary>
public sealed class FeatureSelectionContext : IFeatureSelectionContext
{
    public IAssetProvider             Repository       { get; init; } = null!;
    public IServiceOrder?               TargetOrder      { get; init; }
    public IServiceOrderRepository?     OrderRepository  { get; init; }
    public IReadOnlyDictionary<string, object> Parameters { get; init; } =
        new Dictionary<string, object>(StringComparer.Ordinal);

    /// <summary>
    /// Defaults to an <see cref="AssetProviderSpatialQuery"/> over <see cref="Repository"/>, created
    /// once on first use; set it to route strategies to a host-supplied implementation.
    /// </summary>
    public IAssetSpatialQuery SpatialQuery
    {
        get => field ??= new AssetProviderSpatialQuery(Repository);
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    }

    public Guid? OrganizationId { get; init; }
}
