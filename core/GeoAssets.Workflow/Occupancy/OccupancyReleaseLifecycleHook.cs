using GeoAssets.Workflow.Orders;

namespace GeoAssets.Workflow.Occupancy;

/// <summary>
/// The NeutralOperator vertical's effect of closing an order (XD01-315, as a lifecycle hook since XD01-362 — it used to be
/// hard-coded in the generic order repository): a closed order can never commit what is still staged on its targets, so
/// those change sets are discarded at the closing instant and in the same <c>SaveChanges</c> as the closing transition. Sets
/// already committed — the approval path marks them before it closes the order — or discarded are not touched.
/// </summary>
public sealed class OccupancyReleaseLifecycleHook(IOccupancyChangeSetCloser changeSets) : IOrderLifecycleHook
{
    public async Task OnTransitionedAsync(OrderTransitionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.IsTerminal)
            await changeSets.DiscardStagedAsync(context.Order.Id, context.OccurredAt, cancellationToken).ConfigureAwait(false);
    }
}
