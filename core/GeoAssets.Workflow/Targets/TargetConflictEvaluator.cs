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

        return Evaluate(new OrderIdentity(TargetLedger.KernelVerticalId, order.Id, order.ParentOrderId), ownExclusivity, featureIds, usages);
    }

    /// <summary>
    /// <see cref="Evaluate(IServiceOrder, OrderTargetExclusivity, IReadOnlyCollection{string}, IReadOnlyCollection{OrderTargetUsage})"/>
    /// for an order of the global target ledger (XD01-379), which may belong to a remote vertical: the kernel holds no
    /// <see cref="IServiceOrder"/> for it, and a remote order has no family the kernel knows of. A usage counts as the
    /// order itself only when it names the same <see cref="OrderTargetUsage.VerticalId"/> and order id; every usage of
    /// another vertical counts, whatever its order id.
    /// </summary>
    /// <exception cref="ArgumentNullException">A reference argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">As the in-process overload.</exception>
    public static IReadOnlyDictionary<string, OrderTargetConflictLevel> Evaluate(
        TargetLedgerOrder order,
        IReadOnlyCollection<string> featureIds,
        IReadOnlyCollection<OrderTargetUsage> usages)
    {
        ArgumentNullException.ThrowIfNull(order);

        return Evaluate(new OrderIdentity(order.VerticalId, order.OrderId, ParentOrderId: null), order.Policy.Exclusivity, featureIds, usages);
    }

    private static Dictionary<string, OrderTargetConflictLevel> Evaluate(
        OrderIdentity order,
        OrderTargetExclusivity ownExclusivity,
        IReadOnlyCollection<string> featureIds,
        IReadOnlyCollection<OrderTargetUsage> usages)
    {
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

    /// <summary>
    /// Whether <paramref name="usage"/> is held by <paramref name="order"/> itself or by its family. Orders of different
    /// verticals are never one family, whatever their ids.
    /// </summary>
    internal static bool IsOwnOrSameFamily(OrderIdentity order, OrderTargetUsage usage)
    {
        ArgumentNullException.ThrowIfNull(usage);

        if (!SameId(usage.VerticalId, order.VerticalId)) return false;
        if (SameId(usage.OrderId, order.Id)) return true;                              // the order itself
        if (order.ParentOrderId is { } parentId && SameId(usage.OrderId, parentId)) return true; // its parent
        if (usage.ParentOrderId is not { } usageParentId) return false;

        return SameId(usageParentId, order.Id)                                          // its child
            || (order.ParentOrderId is { } ownParentId && SameId(usageParentId, ownParentId)); // a sibling
    }

    /// <summary>Who an order is, for telling its own and its family's usages from the others'.</summary>
    internal readonly record struct OrderIdentity(string VerticalId, string Id, string? ParentOrderId);

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
