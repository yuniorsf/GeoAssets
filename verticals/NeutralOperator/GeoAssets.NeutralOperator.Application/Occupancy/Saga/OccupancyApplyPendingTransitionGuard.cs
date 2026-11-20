using GeoAssets.Workflow.Orders;

namespace GeoAssets.NeutralOperator.Application.Occupancy.Saga;

/// <summary>
/// Holds an order in its "approved, awaiting apply" sub-state (XD01-383): while its attributes say
/// <see cref="OccupancyApplySagaAttributes.AwaitingApply"/>, every status transition is refused except the approval's own
/// (which the saga makes when the kernel applies the batch) — no cancel, reject or rework under a batch in flight, whose
/// occupancy a later rejection would have to restore. Generic over order types: it reads only the saga's attribute and
/// the type's approval transition.
/// </summary>
public sealed class OccupancyApplyPendingTransitionGuard : ITransitionGuard
{
    /// <summary>The denial's code.</summary>
    public const string ApplyPendingCode = OccupancyProblemCodes.ApplyPending;

    public Task<GuardVerdict> EvaluateAsync(OrderTransitionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!OccupancyApplySagaAttributes.IsAwaitingApply(context.Order.Attributes))
            return Task.FromResult(GuardVerdict.Allow);

        var approved = OccupancyApproval.ApprovedStatus(context.OrderType, context.FromStatus);
        return Task.FromResult(string.Equals(context.ToStatus, approved, StringComparison.Ordinal)
            ? GuardVerdict.Allow
            : GuardVerdict.Deny(
                ApplyPendingCode,
                "The order is approved and awaits the asset catalog's apply of its occupancy; it cannot change status until the catalog answers."));
    }
}
