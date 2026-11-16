namespace GeoAssets.Workflow.Targets;

/// <summary>
/// Opts an <see cref="Orders.OrderType"/> into per-target work: which asset types an order of
/// that type may target, how many, which activities apply to each target, and how overlaps with
/// other open orders are treated. An order type whose
/// <see cref="Orders.OrderType.TargetPolicy"/> is null keeps the flat feature-id behaviour.
/// </summary>
public sealed record OrderTargetPolicy
{
    /// <summary>Asset type ids an order of the owning type may target. Must not be empty.</summary>
    public required IReadOnlyList<string> TargetAssetTypeIds { get; init; }

    /// <summary>
    /// Attribute key whose value is shown as each target's alias. Null means "no alias";
    /// a blank value is rejected.
    /// </summary>
    public string? AliasAttributeKey { get; init; }

    /// <summary>
    /// Maximum number of targets one selection may add. Null means unlimited; zero or a
    /// negative value is rejected.
    /// </summary>
    public int? SelectionLimit { get; init; }

    /// <summary>Activities that can be assigned to each target. Ids must be unique.</summary>
    public IReadOnlyList<OrderTargetActivity> Activities { get; init; } = [];

    public OrderTargetExclusivity Exclusivity { get; init; } = OrderTargetExclusivity.None;

    /// <summary>Null means orders of the owning type are never fanned out into child orders.</summary>
    public OrderTargetFanOutPolicy? FanOut { get; init; }

    /// <summary>
    /// Checks this policy's own invariants. Ids are compared case-insensitively, matching
    /// <see cref="Orders.OrderTypeRegistry"/>'s own lookup.
    /// </summary>
    /// <param name="owningOrderTypeId">Id of the order type this policy is attached to.</param>
    /// <exception cref="ArgumentException">
    /// <see cref="TargetAssetTypeIds"/> is empty, two <see cref="Activities"/> share an id,
    /// <see cref="AliasAttributeKey"/> is blank, or <see cref="FanOut"/>'s child order type id
    /// is blank or is <paramref name="owningOrderTypeId"/> itself.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="SelectionLimit"/> is zero or negative.</exception>
    public void Validate(string owningOrderTypeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owningOrderTypeId);

        EnsureTargetAssetTypes(TargetAssetTypeIds, owningOrderTypeId);
        EnsureSelectionLimit(SelectionLimit, owningOrderTypeId);
        EnsureUniqueActivityIds(Activities, owningOrderTypeId);
        EnsureAliasAttributeKey(AliasAttributeKey, owningOrderTypeId);
        EnsureFanOut(FanOut, owningOrderTypeId);
    }

    private static void EnsureTargetAssetTypes(IReadOnlyList<string>? targetAssetTypeIds, string owningOrderTypeId)
    {
        if (targetAssetTypeIds is { Count: > 0 }) return;

        throw new ArgumentException(
            $"OrderType '{owningOrderTypeId}': a target policy must name at least one target asset type.",
            nameof(targetAssetTypeIds));
    }

    private static void EnsureSelectionLimit(int? selectionLimit, string owningOrderTypeId)
    {
        if (selectionLimit is null or > 0) return;

        throw new ArgumentOutOfRangeException(
            nameof(selectionLimit),
            selectionLimit,
            $"OrderType '{owningOrderTypeId}': the selection limit must be positive, or null for unlimited.");
    }

    private static void EnsureUniqueActivityIds(IReadOnlyList<OrderTargetActivity>? activities, string owningOrderTypeId)
    {
        if (activities is null) return;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var activity in activities)
        {
            if (seen.Add(activity.Id)) continue;

            throw new ArgumentException(
                $"OrderType '{owningOrderTypeId}': target activity id '{activity.Id}' is declared more than once.",
                nameof(activities));
        }
    }

    private static void EnsureAliasAttributeKey(string? aliasAttributeKey, string owningOrderTypeId)
    {
        if (aliasAttributeKey is null || !string.IsNullOrWhiteSpace(aliasAttributeKey)) return;

        throw new ArgumentException(
            $"OrderType '{owningOrderTypeId}': the alias attribute key must be null or non-blank.",
            nameof(aliasAttributeKey));
    }

    private static void EnsureFanOut(OrderTargetFanOutPolicy? fanOut, string owningOrderTypeId)
    {
        if (fanOut is null) return;

        if (string.IsNullOrWhiteSpace(fanOut.ChildOrderTypeId))
        {
            throw new ArgumentException(
                $"OrderType '{owningOrderTypeId}': the fan-out child order type id must not be blank.",
                nameof(fanOut));
        }

        if (string.Equals(fanOut.ChildOrderTypeId, owningOrderTypeId, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"OrderType '{owningOrderTypeId}': an order type cannot fan out into itself.",
                nameof(fanOut));
        }
    }
}
