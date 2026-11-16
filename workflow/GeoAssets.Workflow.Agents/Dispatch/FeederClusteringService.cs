using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Services;
using GeoAssets.Workflow.Dispatch;

namespace GeoAssets.Workflow.Agents.Dispatch;

/// <summary>
/// Groups open orders by their upstream feeder/pressure-zone (XD01-240), using the same
/// <see cref="TopoGraph"/> traversal <c>GeoAssets.Workflow.Scada.FeederOutageDispatchService</c>
/// (XD01-239) already relies on for downstream impact, just walked upstream instead:
/// <see cref="IAssetProvider.GetAncestors(string)"/> finds every feature that can reach an
/// order's feature, filtered down to ones typed
/// <see cref="FeederClusterDispatchTypes.FeederAssetTypeId"/>. <see cref="IAssetProvider"/>
/// doesn't expose an edge-kind-filtered ancestor query (only <c>GetDescendants</c> does), so
/// when more than one feeder ancestor exists the geodetically nearest one is used as a
/// practical proxy for "the one actually feeding it" — a real deployment's topology is
/// expected to have exactly one.  An order whose feature has no feeder ancestor falls back to
/// being its own single-order cluster, keyed by its own feature ID, so it's never dropped.
/// </summary>
public static class FeederClusteringService
{
    /// <summary>Clusters <paramref name="orders"/> by feeder-ancestor feature ID. The
    /// dictionary key is the feeder (or fallback feature) ID; values preserve input order.</summary>
    public static IReadOnlyDictionary<string, List<ServiceOrderWithLocation>> Cluster(
        IReadOnlyList<ServiceOrderWithLocation> orders, IAssetProvider assetProvider)
    {
        ArgumentNullException.ThrowIfNull(orders);
        ArgumentNullException.ThrowIfNull(assetProvider);

        var clusters = new Dictionary<string, List<ServiceOrderWithLocation>>(StringComparer.Ordinal);

        foreach (var order in orders)
        {
            var clusterKey = ResolveFeederClusterKey(order.FeatureId, order.Location, assetProvider);
            if (!clusters.TryGetValue(clusterKey, out var members))
            {
                members = [];
                clusters[clusterKey] = members;
            }
            members.Add(order);
        }

        return clusters;
    }

    private static string ResolveFeederClusterKey(
        string featureId, GeoAssets.Core.Models.Geometry.GeoPoint orderLocation, IAssetProvider assetProvider)
    {
        var feederAncestors = assetProvider
            .GetAncestors(featureId)
            .Where(f => string.Equals(f.Properties.AssetTypeId, FeederClusterDispatchTypes.FeederAssetTypeId, StringComparison.Ordinal))
            .ToList();

        if (feederAncestors.Count == 0)
            return featureId;

        return feederAncestors
            .OrderBy(f => f.Geometry is null
                ? double.MaxValue
                : GeodeticDistanceCalculator.DistanceInMeters(orderLocation, f.Geometry.Centroid))
            .First()
            .Id;
    }
}
