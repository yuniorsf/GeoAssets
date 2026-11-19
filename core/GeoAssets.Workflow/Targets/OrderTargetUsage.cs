namespace GeoAssets.Workflow.Targets;

/// <summary>
/// States that another open order currently holds a feature as one of its targets. Returned by
/// <see cref="IServiceOrderTargetReader.FindOpenUsagesAsync"/> and consumed by
/// <see cref="TargetConflictEvaluator"/>.
/// </summary>
/// <param name="FeatureId">The feature held.</param>
/// <param name="OrderId">The order holding it.</param>
/// <param name="OrderTypeId">That order's type.</param>
/// <param name="OrderStatus">
/// That order's current status key; empty for an order of a remote vertical, whose lifecycle the kernel does not hold.
/// </param>
/// <param name="ParentOrderId">That order's parent, or null when it is a root order (always null for a remote vertical's order).</param>
/// <param name="Exclusivity">
/// The <see cref="OrderTargetPolicy.Exclusivity"/> of that order's type;
/// <see cref="OrderTargetExclusivity.None"/> when the type is unknown or has no target policy. For a remote vertical's
/// order, the exclusivity its type declared when the target was reserved.
/// </param>
/// <param name="VerticalId">
/// The vertical that owns <paramref name="OrderId"/> (XD01-379); <see cref="TargetLedger.KernelVerticalId"/> for an
/// in-process order.
/// </param>
public sealed record OrderTargetUsage(
    string FeatureId,
    string OrderId,
    string OrderTypeId,
    string OrderStatus,
    string? ParentOrderId,
    OrderTargetExclusivity Exclusivity,
    string VerticalId = TargetLedger.KernelVerticalId);
