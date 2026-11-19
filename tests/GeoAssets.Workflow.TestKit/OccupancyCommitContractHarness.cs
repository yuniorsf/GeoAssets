using GeoAssets.NeutralOperator.Domain.Occupancy;
using GeoAssets.NeutralOperator.Application.Occupancy;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Targets;
using Microsoft.Extensions.Time.Testing;

namespace GeoAssets.Workflow.TestKit;

/// <summary>
/// Disposable test harness bundle for <see cref="OccupancyCommitContractTests"/> implementations
/// (XD01-318): the approval under test and every store it reads and writes — so the suite can
/// seed orders, targets, live occupancy and staged sets the way production code does, and look at
/// what an approval left — plus a way to read the asset mutation batches it staged on the outbox.
/// </summary>
/// <param name="Service">The approval under test.</param>
/// <param name="Orders">The order store.</param>
/// <param name="TargetReader">The target store, read side.</param>
/// <param name="TargetWriter">The target store, write side.</param>
/// <param name="OccupancyReader">The live occupancy store, read side, scoped to <paramref name="OwnerOrganizationId"/>.</param>
/// <param name="OccupancyWriter">The live occupancy store, write side, scoped to <paramref name="OwnerOrganizationId"/>.</param>
/// <param name="ChangeSetReader">The staged-change store, read side, scoped to <paramref name="OwnerOrganizationId"/>.</param>
/// <param name="ChangeSetWriter">The staged-change store, write side, scoped to <paramref name="OwnerOrganizationId"/>.</param>
/// <param name="OrderTypes">The registry the approval resolves order types from; the suite registers its types here.</param>
/// <param name="Clock">The clock the approval and the stores read.</param>
/// <param name="OwnerOrganizationId">The pole owner every store is scoped to.</param>
/// <param name="CatalogInstanceId">The catalog instance the approval is configured to address.</param>
/// <param name="StagedBatchesAsync">Every asset mutation batch staged so far, in the order staged.</param>
/// <param name="Scope">Whatever must be disposed with the harness (a database, a connection).</param>
public sealed record OccupancyCommitContractHarness(
    IOccupancyCommitService Service,
    IServiceOrderRepository Orders,
    IServiceOrderTargetReader TargetReader,
    IServiceOrderTargetWriter TargetWriter,
    IPoleOccupancyReader OccupancyReader,
    IPoleOccupancyWriter OccupancyWriter,
    IOccupancyChangeSetReader ChangeSetReader,
    IOccupancyChangeSetWriter ChangeSetWriter,
    OrderTypeRegistry OrderTypes,
    FakeTimeProvider Clock,
    Guid OwnerOrganizationId,
    string CatalogInstanceId,
    Func<CancellationToken, Task<IReadOnlyList<StagedAssetMutationBatch>>> StagedBatchesAsync,
    IAsyncDisposable? Scope = null) : IAsyncDisposable
{
    public ValueTask DisposeAsync() => Scope?.DisposeAsync() ?? ValueTask.CompletedTask;
}
