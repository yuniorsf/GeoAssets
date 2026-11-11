using GeoAssets.Workflow.Dispatch;

namespace GeoAssets.Workflow.Agents.Dispatch;

/// <summary>
/// Pure yield-ranking logic for XD01-256's dynamic dispatch advisor: sorts orders so that no
/// order whose SLA deadline (<see cref="DispatchableOrder.TimeWindowEnd"/>) falls within
/// <paramref name="deadlineUrgencyWindow"/> of <paramref name="now"/> is ever ranked below a
/// non-urgent order, however low its predicted value — "no SLA-breaching ticket is
/// deprioritized" per this ticket's AC, read literally as a ranking invariant rather than a
/// routing constraint (routing/time-window feasibility is <c>ICrewRoutingEngine</c>'s job).
/// Urgent orders break ties by soonest deadline first; non-urgent orders break ties by highest
/// <see cref="YieldRankedOrder.PredictedValueUs"/> first.
/// </summary>
public static class OrderYieldRanker
{
    public static IReadOnlyList<YieldRankedOrder> Rank(
        IReadOnlyList<YieldRankedOrder> orders, DateTimeOffset now, TimeSpan deadlineUrgencyWindow)
    {
        ArgumentNullException.ThrowIfNull(orders);

        return [.. orders
            .OrderBy(o => IsUrgent(o, now, deadlineUrgencyWindow) ? 0 : 1)
            .ThenBy(o => o.Order.TimeWindowEnd ?? DateTimeOffset.MaxValue)
            .ThenByDescending(o => o.PredictedValueUs)];
    }

    /// <summary>Whether <paramref name="order"/>'s deadline has already passed or falls within
    /// <paramref name="deadlineUrgencyWindow"/> of <paramref name="now"/> — the same test
    /// <see cref="Rank"/> uses to decide ranking priority, exposed so a caller building the
    /// advisory result can flag the same orders as deadline-critical without re-deriving the
    /// rule differently.</summary>
    public static bool IsUrgent(YieldRankedOrder order, DateTimeOffset now, TimeSpan deadlineUrgencyWindow)
    {
        ArgumentNullException.ThrowIfNull(order);
        return order.Order.TimeWindowEnd is { } deadline && deadline <= now + deadlineUrgencyWindow;
    }
}
