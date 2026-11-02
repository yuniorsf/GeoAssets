using System.Globalization;
using GeoAssets.Core.Isp;
using GeoAssets.Core.Messaging.Events;
using GeoAssets.Core.Models;

namespace GeoAssets.Workflow.FieldWork;

/// <summary>
/// Builds the single <see cref="AssetFeatureMutationItem"/> that installing/removing a drop at a
/// CTO submits as part of one <c>AssetMutationBatchRequestedEvent</c> (XD01-281's AC: "updates the
/// owning CTO's occupied-port count atomically") — atomicity here is the same guarantee
/// <c>IGeoCoreClient.ExecuteAssetDeltaBatchAsync</c>'s own contract already promises for any batch
/// submitted through it, rather than a new compare-and-swap primitive on <c>IAssetProvider</c>.
/// </summary>
public static class CtoOccupancyMutationFactory
{
    public static CtoOccupancyAdjustmentResult BuildAdjustmentMutation(GeoFeature ctoFeature, int delta)
    {
        ArgumentNullException.ThrowIfNull(ctoFeature);

        var (occupied, total) = CtoCapacityCalculator.GetOccupiedAndTotal(ctoFeature.Properties.CustomAttributes);
        var newOccupied = occupied + delta;

        if (newOccupied < 0)
        {
            return CtoOccupancyAdjustmentResult.Failure(
                $"Adjustment would drop occupied ports below zero ({occupied} + {delta} = {newOccupied}).");
        }

        if (total > 0 && newOccupied > total)
        {
            return CtoOccupancyAdjustmentResult.Failure(
                $"Adjustment would exceed total splitter capacity ({newOccupied} > {total}).");
        }

        var mutation = new AssetFeatureMutationItem(
            FeatureId: ctoFeature.Id,
            MutationKind: AssetMutationKind.UpdateAttributes,
            Name: "",
            AssetTypeId: ctoFeature.Properties.AssetTypeId,
            Description: "",
            LayerId: "",
            Geometry: null,
            TopologyEdges: [],
            CustomAttributes: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [CtoCapacityCalculator.OccupiedSplitterPortsKey] = newOccupied.ToString(CultureInfo.InvariantCulture),
            });

        return CtoOccupancyAdjustmentResult.Success(mutation, newOccupied);
    }
}
