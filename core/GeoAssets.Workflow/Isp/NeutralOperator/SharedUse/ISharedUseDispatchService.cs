using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;

namespace GeoAssets.Workflow.Isp.NeutralOperator.SharedUse;

/// <summary>
/// Dispatches a shared-use order to the occupants of its poles (XD01-316): the write side.
/// <see cref="ISharedUseDispatchPlanReader"/> is the read side.
/// </summary>
public interface ISharedUseDispatchService
{
    /// <summary>
    /// Creates one child order per occupant organization with an active contract on the order's
    /// poles, dispatches each to its organization, stages one notification per child and moves
    /// the parent along its dispatch transition — all of it or none of it.
    ///
    /// Repeating the call is safe: a child's id depends only on the parent and the occupant, so a
    /// recipient that already has its child gets neither a second one nor a second notification,
    /// and an occupant that appeared since (poles were added) gets its child. A call that finds
    /// nothing to create writes nothing.
    /// </summary>
    /// <param name="orderId">The order to dispatch.</param>
    /// <param name="expectedRowVersion">
    /// The order's <see cref="IServiceOrder.RowVersion"/> as the caller last read it; empty for
    /// "no precondition", as for <see cref="IServiceOrderWriter.UpdateAsync"/>.
    /// </param>
    /// <param name="principal">The caller: must be allowed <see cref="OrderActionType.Dispatch"/> and belong to the order's organization.</param>
    /// <exception cref="KeyNotFoundException">No such order; or its type, or the child type it fans out into, is not registered (<see cref="OrderTypeNotRegisteredException"/>).</exception>
    /// <exception cref="OrderActionDeniedException">The rules refuse the dispatch, or the caller is not of the order's organization.</exception>
    /// <exception cref="OrderTypeHasNoFanOutException">The order's type declares no fan-out.</exception>
    /// <exception cref="InvalidServiceOrderTransitionException">The order cannot be dispatched from its status.</exception>
    /// <exception cref="SharedUseDispatchBlockedException">The order cannot be dispatched as it stands.</exception>
    /// <exception cref="ServiceOrderConcurrencyException">The order changed since <paramref name="expectedRowVersion"/> was read.</exception>
    Task<SharedUseDispatchResult> DispatchAsync(
        string orderId, byte[] expectedRowVersion, WorkflowPrincipal principal, CancellationToken ct);
}
