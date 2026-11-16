using GeoAssets.Workflow.Orders;

namespace GeoAssets.Workflow.Targets;

/// <summary>
/// Decides, for each feature an order targets, how strongly it conflicts with the other open
/// orders holding the same feature. Pure: no I/O, no clock.
/// </summary>
public static class TargetConflictEvaluator
{
    /// <summary>
    /// Returns the conflict level of every feature in <paramref name="featureIds"/> for
    /// <paramref name="order"/> — one entry per distinct feature id, including those with
    /// <see cref="OrderTargetConflictLevel.None"/>.
    ///
    /// A usage is ignored when it belongs to <paramref name="order"/> itself or to its family:
    /// its parent, one of its children, or a sibling (an order sharing the same non-null
    /// parent). After a fan-out, a parent and its children hold the same features by
    /// construction, so counting them would make every target conflict with itself. Usages of
    /// features not in <paramref name="featureIds"/> are ignored too.
    ///
    /// For a feature with at least one remaining usage, the level is the stronger of
    /// <paramref name="ownExclusivity"/> and the strongest
    /// <see cref="OrderTargetUsage.Exclusivity"/> among those usages: any
    /// <see cref="OrderTargetExclusivity.BlockOnOverlap"/> gives
    /// <see cref="OrderTargetConflictLevel.Blocked"/>, otherwise any
    /// <see cref="OrderTargetExclusivity.WarnOnOverlap"/> gives
    /// <see cref="OrderTargetConflictLevel.Warning"/>, otherwise
    /// <see cref="OrderTargetConflictLevel.None"/>. A feature with no remaining usage is
    /// <see cref="OrderTargetConflictLevel.None"/> whatever <paramref name="ownExclusivity"/> is.
    /// </summary>
    /// <param name="order">The order being evaluated.</param>
    /// <param name="ownExclusivity">The <see cref="OrderTargetPolicy.Exclusivity"/> of <paramref name="order"/>'s type.</param>
    /// <param name="featureIds">The features to evaluate.</param>
    /// <param name="usages">
    /// Open usages of those features, as <see cref="IServiceOrderTargetReader.FindOpenUsagesAsync"/> returns them.
    /// </param>
    /// <exception cref="ArgumentNullException">A reference argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="ownExclusivity"/>, or the exclusivity of any usage, is not a defined
    /// <see cref="OrderTargetExclusivity"/> value.
    /// </exception>
    public static IReadOnlyDictionary<string, OrderTargetConflictLevel> Evaluate(
        IServiceOrder order,
        OrderTargetExclusivity ownExclusivity,
        IReadOnlyCollection<string> featureIds,
        IReadOnlyCollection<OrderTargetUsage> usages)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(featureIds);
        ArgumentNullException.ThrowIfNull(usages);

        var ownLevel = ToLevel(ownExclusivity);

        var levels = new Dictionary<string, OrderTargetConflictLevel>(StringComparer.Ordinal);
        foreach (var featureId in featureIds)
            levels[featureId] = OrderTargetConflictLevel.None;

        foreach (var usage in usages)
        {
            var otherLevel = ToLevel(usage.Exclusivity);

            if (!levels.TryGetValue(usage.FeatureId, out var current)) continue;
            if (IsOwnOrSameFamily(order, usage)) continue;

            levels[usage.FeatureId] = Stronger(current, Stronger(ownLevel, otherLevel));
        }

        return levels;
    }

    private static bool IsOwnOrSameFamily(IServiceOrder order, OrderTargetUsage usage)
    {
        if (SameId(usage.OrderId, order.Id)) return true;                              // the order itself
        if (order.ParentOrderId is { } parentId && SameId(usage.OrderId, parentId)) return true; // its parent
        if (usage.ParentOrderId is not { } usageParentId) return false;

        return SameId(usageParentId, order.Id)                                          // its child
            || (order.ParentOrderId is { } ownParentId && SameId(usageParentId, ownParentId)); // a sibling
    }

    private static bool SameId(string left, string right) => string.Equals(left, right, StringComparison.Ordinal);

    private static OrderTargetConflictLevel Stronger(OrderTargetConflictLevel left, OrderTargetConflictLevel right)
        => left >= right ? left : right;

    private static OrderTargetConflictLevel ToLevel(OrderTargetExclusivity exclusivity) => exclusivity switch
    {
        OrderTargetExclusivity.None => OrderTargetConflictLevel.None,
        OrderTargetExclusivity.WarnOnOverlap => OrderTargetConflictLevel.Warning,
        OrderTargetExclusivity.BlockOnOverlap => OrderTargetConflictLevel.Blocked,
        _ => throw new ArgumentOutOfRangeException(nameof(exclusivity), exclusivity, "Not a defined target exclusivity."),
    };
}
