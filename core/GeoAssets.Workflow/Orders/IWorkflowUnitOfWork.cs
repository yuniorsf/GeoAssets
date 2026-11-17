namespace GeoAssets.Workflow.Orders;

/// <summary>
/// Runs several writes against the workflow stores — orders, their targets, the outbox — as one
/// unit: all of them are stored, or none is (XD01-316).
///
/// The stores taking part are the ones registered beside this port in the same scope; an adapter
/// over a database joins them through that scope's connection. When the caller already runs
/// inside a unit of work of the same scope, the work joins it and the outer one decides the
/// outcome.
///
/// A write lost to a concurrent writer of the same order surfaces as
/// <see cref="ServiceOrderConcurrencyException"/>, whichever store reported it. Any exception —
/// cancellation included — leaves nothing of the work stored and is rethrown.
/// </summary>
public interface IWorkflowUnitOfWork
{
    Task<TResult> ExecuteAsync<TResult>(Func<CancellationToken, Task<TResult>> work, CancellationToken ct);
}
