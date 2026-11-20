namespace GeoAssets.NeutralOperator.Application.Occupancy.Saga;

/// <summary>
/// The order attributes that carry an occupancy approval's apply saga on the order itself (XD01-383) — the
/// "approved, awaiting apply" sub-state: the order keeps its status (the one approving it left) while
/// <see cref="StateKey"/> is <see cref="AwaitingApply"/>, and moves to its approved status only when the kernel applied the
/// batch. Data the engine already stores and serves on every order read, so a client can show the pending state; no order
/// type is special-cased in generic code.
/// </summary>
public static class OccupancyApplySagaAttributes
{
    /// <summary>The attribute holding the saga's state on the order.</summary>
    public const string StateKey = "occupancy.apply.state";

    /// <summary>The attribute holding the saga's batch id on the order.</summary>
    public const string BatchIdKey = "occupancy.apply.batch-id";

    /// <summary>
    /// The attribute naming, comma-separated in ordinal order, the poles a reverted saga's compensation left untouched
    /// because a later change superseded the approval (XD01-392); absent when every pole was restored.
    /// </summary>
    public const string UnrestoredKey = "occupancy.apply.unrestored";

    /// <summary>Approved; the kernel has not applied the batch yet. Every transition but the approval's is refused meanwhile.</summary>
    public const string AwaitingApply = "awaiting-apply";

    /// <summary>The kernel applied the batch; the order moved to its approved status.</summary>
    public const string Applied = "applied";

    /// <summary>The kernel rejected the batch; the occupancy was restored and the order is where it was before the approval.</summary>
    public const string Reverted = "reverted";

    /// <summary>Whether <paramref name="attributes"/> say the order awaits the kernel's apply.</summary>
    public static bool IsAwaitingApply(IReadOnlyDictionary<string, string> attributes)
    {
        ArgumentNullException.ThrowIfNull(attributes);
        return attributes.TryGetValue(StateKey, out var state) && string.Equals(state, AwaitingApply, StringComparison.Ordinal);
    }
}
