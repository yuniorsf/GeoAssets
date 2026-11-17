using GeoAssets.Workflow.Orders;

namespace GeoAssets.Workflow.TestKit;

/// <summary>
/// An <see cref="IWorkflowUnitOfWork"/> for tests on in-memory stores (XD01-316): it runs the
/// work and counts the units. In-memory stores have nothing to roll back to, so — unlike a
/// database adapter — what the work wrote before failing stays written; atomicity is asserted
/// against the EF Core adapter, not here.
/// </summary>
public sealed class InMemoryWorkflowUnitOfWork : IWorkflowUnitOfWork
{
    /// <summary>How many units were started.</summary>
    public int Started { get; private set; }

    /// <summary>How many units ran to the end without throwing.</summary>
    public int Completed { get; private set; }

    public async Task<TResult> ExecuteAsync<TResult>(Func<CancellationToken, Task<TResult>> work, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(work);
        ct.ThrowIfCancellationRequested();

        Started++;
        var result = await work(ct);
        Completed++;
        return result;
    }
}
