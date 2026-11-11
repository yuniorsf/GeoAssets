using FluentAssertions;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Workflow.Agents.Dispatch;
using GeoAssets.Workflow.Dispatch;
using Xunit;

namespace GeoAssets.Workflow.Agents.Tests.Dispatch;

/// <summary>
/// XD01-256's AC: "Yield optimizer ranks open tickets by predicted US value while respecting
/// hard SLA deadlines (no SLA-breaching ticket is deprioritized)".
/// </summary>
public class OrderYieldRankerTests
{
    private static readonly GeoPoint SomePoint = new(-46.6, -23.5);
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 8, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan UrgencyWindow = TimeSpan.FromHours(24);

    private static YieldRankedOrder Order(string id, decimal predictedValueUs, DateTimeOffset? deadline = null) =>
        new(new DispatchableOrder(
                id, SomePoint, new HashSet<string>(StringComparer.Ordinal), new HashSet<string>(StringComparer.Ordinal),
                TimeSpan.FromMinutes(30), deadline),
            predictedValueUs);

    [Fact]
    public void Rank_NoUrgentOrders_SortsByPredictedValueDescending()
    {
        var orders = new[] { Order("low", 10), Order("high", 100), Order("mid", 50) };

        var ranked = OrderYieldRanker.Rank(orders, Now, UrgencyWindow);

        ranked.Select(o => o.Order.OrderId).Should().Equal("high", "mid", "low");
    }

    [Fact]
    public void Rank_UrgentLowValueOrder_IsNeverRankedBelowNonUrgentHigherValueOrder()
    {
        var urgent = Order("urgent-low-value", predictedValueUs: 1, deadline: Now.AddHours(2));
        var nonUrgentHighValue = Order("non-urgent-high-value", predictedValueUs: 10_000);

        var ranked = OrderYieldRanker.Rank([nonUrgentHighValue, urgent], Now, UrgencyWindow);

        ranked.Select(o => o.Order.OrderId).Should().Equal("urgent-low-value", "non-urgent-high-value");
    }

    [Fact]
    public void Rank_AlreadyOverdueOrder_IsTreatedAsUrgent()
    {
        var overdue = Order("overdue", predictedValueUs: 0, deadline: Now.AddHours(-1));
        var nonUrgent = Order("non-urgent", predictedValueUs: 5);

        var ranked = OrderYieldRanker.Rank([nonUrgent, overdue], Now, UrgencyWindow);

        ranked.Select(o => o.Order.OrderId).Should().Equal("overdue", "non-urgent");
    }

    [Fact]
    public void Rank_MultipleUrgentOrders_BreaksTiesBySoonestDeadlineFirst()
    {
        var soonest = Order("soonest", predictedValueUs: 0, deadline: Now.AddHours(1));
        var later = Order("later", predictedValueUs: 1_000, deadline: Now.AddHours(20));

        var ranked = OrderYieldRanker.Rank([later, soonest], Now, UrgencyWindow);

        ranked.Select(o => o.Order.OrderId).Should().Equal("soonest", "later");
    }

    [Fact]
    public void Rank_OrderWithNoDeadline_IsNeverTreatedAsUrgent()
    {
        var noDeadline = Order("no-deadline", predictedValueUs: 1);

        OrderYieldRanker.IsUrgent(noDeadline, Now, UrgencyWindow).Should().BeFalse();
    }

    [Fact]
    public void IsUrgent_DeadlineExactlyAtWindowBoundary_IsUrgent()
    {
        var atBoundary = Order("at-boundary", predictedValueUs: 1, deadline: Now + UrgencyWindow);

        OrderYieldRanker.IsUrgent(atBoundary, Now, UrgencyWindow).Should().BeTrue();
    }

    [Fact]
    public void IsUrgent_DeadlineJustBeyondWindow_IsNotUrgent()
    {
        var justBeyond = Order("just-beyond", predictedValueUs: 1, deadline: Now + UrgencyWindow + TimeSpan.FromSeconds(1));

        OrderYieldRanker.IsUrgent(justBeyond, Now, UrgencyWindow).Should().BeFalse();
    }

    [Fact]
    public void Rank_EmptyList_ReturnsEmpty()
    {
        OrderYieldRanker.Rank([], Now, UrgencyWindow).Should().BeEmpty();
    }

    [Fact]
    public void Rank_NullOrders_Throws()
    {
        var act = () => OrderYieldRanker.Rank(null!, Now, UrgencyWindow);
        act.Should().Throw<ArgumentNullException>();
    }
}
