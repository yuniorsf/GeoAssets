using System.Globalization;
using GeoAssets.Core.Isp.NeutralOperator;
using GeoAssets.Core.Messaging.Events;
using GeoAssets.Workflow.Orders;

namespace GeoAssets.Workflow.Isp.NeutralOperator;

/// <summary>
/// Builds the single <see cref="AssetFeatureMutationItem"/> a synced
/// <see cref="PoleCensusOrderTypes.PoleCensusInspectionId"/> submission applies to its target
/// pole's <c>geo_entity</c> row (XD01-260's AC: "the synced submission updates the pole's slot
/// occupancy in geo_entity") — same <c>UpdateAttributes</c>-only shape
/// <see cref="FieldWork.CtoOccupancyMutationFactory"/> already established for CTO port counts.
///
/// Runs <see cref="PoleComplianceChecklistProjector.Project"/> first (which writes the four
/// scalar/structured schema fields onto the pole's own <c>CustomAttributes</c> in place), then
/// additionally copies <see cref="PoleSharingComplianceChecklistSchema.OccupantsKey"/>'s raw
/// submitted JSON onto that same dictionary — the projector itself deliberately leaves the
/// occupant array out of <c>CustomAttributes</c> (its own doc comment: that's
/// <see cref="PoleAttachmentSlotMatrix"/>'s job, which "has no EFCore/repository layer yet"), but
/// until that persistence layer exists (the same gap XD01-266/XD01-271 independently flagged),
/// storing the occupant JSON as one more <c>geo_entity</c> custom attribute is the only way this
/// ticket's own literal "updates slot occupancy in geo_entity" AC is actually true end-to-end
/// rather than a value the caller computed and then dropped. A future
/// <c>PoleAttachmentSlotMatrix</c> store can supersede this without changing the schema.
///
/// Also runs <see cref="PoleMechanicalOverloadCalculator"/> (XD01-262) against the pole's
/// <c>CAP_TRC</c> daN rating (the raw BDGD field key from <c>BdgdAssetTypes.Ponnot</c> — ingested
/// onto this same <c>CustomAttributes</c> dictionary by the BDGD ETL, XD01-257) and writes
/// <see cref="PoleMechanicalOverloadCalculator.FlaggedAttributeKey"/>. A pole never run through
/// that ETL (or whose <c>CAP_TRC</c> doesn't parse) has no known rated capacity, so the flag is
/// left unset rather than asserted either way.
/// </summary>
public static class PoleCensusSlotOccupancyMutationFactory
{
    private const string RatedCapacityDaNAttributeKey = "CAP_TRC";

    public static AssetFeatureMutationItem BuildMutation(IServiceOrder order)
    {
        ArgumentNullException.ThrowIfNull(order);

        var matrix = PoleComplianceChecklistProjector.Project(order);
        var pole = order.Features[0];

        var customAttributes = new Dictionary<string, string>(pole.Properties.CustomAttributes, StringComparer.Ordinal)
        {
            [PoleSharingComplianceChecklistSchema.OccupantsKey] = order.Attributes[PoleSharingComplianceChecklistSchema.OccupantsKey],
        };

        if (customAttributes.TryGetValue(RatedCapacityDaNAttributeKey, out var ratedCapacityRaw) &&
            decimal.TryParse(ratedCapacityRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out var ratedCapacityDaN))
        {
            var overload = PoleMechanicalOverloadCalculator.Evaluate(matrix, ratedCapacityDaN);
            customAttributes[PoleMechanicalOverloadCalculator.FlaggedAttributeKey] = overload.IsOverloaded ? "true" : "false";
        }

        return new AssetFeatureMutationItem(
            FeatureId: pole.Id,
            MutationKind: AssetMutationKind.UpdateAttributes,
            Name: "",
            AssetTypeId: pole.Properties.AssetTypeId,
            Description: "",
            LayerId: "",
            Geometry: null,
            TopologyEdges: [],
            CustomAttributes: customAttributes);
    }
}
