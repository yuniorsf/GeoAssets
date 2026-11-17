using GeoAssets.Workflow.Selection.Strategies;
using GeoAssets.Workflow.Targets;

namespace GeoAssets.Shared.Services.OrderTargets;

/// <summary>
/// Turns the map selection into the selection-strategy run that makes it an order's targets
/// (XD01-312). The client never sends the server "these are the targets": it sends either the
/// fence the selection was made with or the selected ids, always restricted to the order type's
/// target asset types, and the server resolves, caps and de-duplicates.
/// </summary>
public static class OrderTargetSelectionPlanner
{
    /// <summary>
    /// Plans the run for <paramref name="policy"/>:
    /// <list type="bullet">
    /// <item>With <paramref name="fenceRings"/> (the selection is exactly one fence's result):
    /// <c>polygon-fence</c> with those rings — the order then keeps the fence for its outline.</item>
    /// <item>Otherwise: <c>identifier-list</c> on the reserved feature-id key, with the selected ids
    /// that are of a targeted asset type or not loaded on the client (the server decides about
    /// those), in ordinal order.</item>
    /// </list>
    /// Both carry <c>assetTypeIds</c> = the policy's target asset types.
    /// </summary>
    /// <exception cref="ArgumentNullException">An argument other than <paramref name="fenceRings"/> is null.</exception>
    public static OrderTargetSelectionPlanResult Plan(
        OrderTargetPolicy policy,
        IReadOnlyCollection<string> selectedIds,
        double[][][]? fenceRings,
        Func<string, string?> assetTypeOf)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(selectedIds);
        ArgumentNullException.ThrowIfNull(assetTypeOf);

        if (selectedIds.Count == 0) return OrderTargetSelectionPlanResult.Failed(OrderTargetSelectionPlanProblem.EmptySelection);

        string[] assetTypeIds = [.. policy.TargetAssetTypeIds];

        if (fenceRings is not null)
        {
            return OrderTargetSelectionPlanResult.Planned(new OrderTargetSelectionPlan(
                PolygonFenceSelectionStrategy.Id,
                new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    [PolygonFenceSelectionStrategy.RingsParameter] = fenceRings,
                    [PolygonFenceSelectionStrategy.AssetTypeIdsParameter] = assetTypeIds,
                },
                IsFence: true,
                IdentifierCount: 0));
        }

        var targeted = assetTypeIds.ToHashSet(StringComparer.Ordinal);
        string[] values =
        [
            .. selectedIds
                .Where(id => assetTypeOf(id) is not { } type || targeted.Contains(type))
                .Order(StringComparer.Ordinal),
        ];

        if (values.Length == 0) return OrderTargetSelectionPlanResult.Failed(OrderTargetSelectionPlanProblem.NoEligibleFeature);
        if (values.Length > IdentifierListSelectionStrategy.MaxDistinctValues)
            return OrderTargetSelectionPlanResult.Failed(OrderTargetSelectionPlanProblem.TooManyIdentifiers);

        return OrderTargetSelectionPlanResult.Planned(new OrderTargetSelectionPlan(
            IdentifierListSelectionStrategy.Id,
            new Dictionary<string, object>(StringComparer.Ordinal)
            {
                [IdentifierListSelectionStrategy.AttributeKeyParameter] = IdentifierListSelectionStrategy.FeatureIdKey,
                [IdentifierListSelectionStrategy.ValuesParameter] = values,
                [IdentifierListSelectionStrategy.AssetTypeIdsParameter] = assetTypeIds,
            },
            IsFence: false,
            IdentifierCount: values.Length));
    }
}
