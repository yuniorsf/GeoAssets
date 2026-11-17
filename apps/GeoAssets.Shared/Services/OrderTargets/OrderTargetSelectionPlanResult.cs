namespace GeoAssets.Shared.Services.OrderTargets;

/// <summary>Either a <see cref="Plan"/> or the <see cref="Problem"/> that prevents one (XD01-312).</summary>
public sealed record OrderTargetSelectionPlanResult
{
    private OrderTargetSelectionPlanResult(OrderTargetSelectionPlan? plan, OrderTargetSelectionPlanProblem? problem)
    {
        Plan = plan;
        Problem = problem;
    }

    /// <summary>The planned run; null when <see cref="Problem"/> is set.</summary>
    public OrderTargetSelectionPlan? Plan { get; }

    /// <summary>Why there is no plan; null when <see cref="Plan"/> is set.</summary>
    public OrderTargetSelectionPlanProblem? Problem { get; }

    public static OrderTargetSelectionPlanResult Planned(OrderTargetSelectionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return new OrderTargetSelectionPlanResult(plan, null);
    }

    public static OrderTargetSelectionPlanResult Failed(OrderTargetSelectionPlanProblem problem) => new(null, problem);
}
