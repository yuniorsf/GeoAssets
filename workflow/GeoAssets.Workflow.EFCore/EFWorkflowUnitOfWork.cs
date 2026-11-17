using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace GeoAssets.Workflow.Persistence;

/// <summary>
/// <see cref="IWorkflowUnitOfWork"/> over the scope's <see cref="ServiceOrderDbContext"/>
/// (XD01-316): the work runs in one database transaction on that context's connection, which
/// every repository registered in the same scope writes through — the order repository, the
/// target store, the occupancy stores and the outbox. That shared context is the whole
/// mechanism; a store built on another context takes no part.
///
/// When the context already has a transaction the work joins it, and whoever opened that one
/// commits or rolls back. Otherwise this class opens one, commits it when the work returns, and
/// on any exception rolls it back and clears the change tracker, so a row a failed write left
/// tracked is not written by a later save in the same scope.
/// </summary>
public sealed class EFWorkflowUnitOfWork(ServiceOrderDbContext db) : IWorkflowUnitOfWork
{
    public async Task<TResult> ExecuteAsync<TResult>(Func<CancellationToken, Task<TResult>> work, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(work);
        ct.ThrowIfCancellationRequested();

        if (db.Database.CurrentTransaction is not null)
            return await RunAsync(work, ct).ConfigureAwait(false);

        var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            try
            {
                var result = await RunAsync(work, ct).ConfigureAwait(false);
                await transaction.CommitAsync(ct).ConfigureAwait(false);
                return result;
            }
            catch
            {
                // Not the caller's token: a cancelled caller is exactly who needs the rollback to run.
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                db.ChangeTracker.Clear();
                throw;
            }
        }
    }

    /// <summary>
    /// Runs the work, turning EF's own lost-write signal into the workflow's: some writers of an
    /// order (<see cref="EFServiceOrderRepository.AppendActionAsync"/>) let it through untranslated.
    /// </summary>
    private static async Task<TResult> RunAsync<TResult>(Func<CancellationToken, Task<TResult>> work, CancellationToken ct)
    {
        try
        {
            return await work(ct).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            var orderId = ex.Entries.Select(entry => entry.Entity).OfType<ServiceOrderRecord>().FirstOrDefault()?.Id;
            throw orderId is null
                ? new ServiceOrderConcurrencyException("A service order was modified by another writer. Reload and retry.", ex)
                : new ServiceOrderConcurrencyException(orderId);
        }
    }
}
