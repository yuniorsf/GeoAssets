using System.Data.Common;
using GeoAssets.Workflow.Persistence;
using Microsoft.EntityFrameworkCore.Storage;

namespace GeoAssets.NeutralOperator.Persistence;

/// <summary>
/// <see cref="IAmbientDbTransaction"/> over the scope's kernel <see cref="ServiceOrderDbContext"/>: the transaction its
/// unit of work (<see cref="EFWorkflowUnitOfWork"/>), its order repository or its outbox has open, so the vertical's
/// context, on the same connection, writes inside it.
/// </summary>
public sealed class ServiceOrderAmbientTransaction(ServiceOrderDbContext kernel) : IAmbientDbTransaction
{
    public DbTransaction? Current => kernel.Database.CurrentTransaction?.GetDbTransaction();
}
