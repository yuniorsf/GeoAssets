using GeoAssets.Core.Isp.NeutralOperator.Occupancy;
using GeoAssets.Workflow.Isp.NeutralOperator.SharedUse;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Targets;
using Microsoft.Extensions.Time.Testing;

namespace GeoAssets.Workflow.TestKit;

/// <summary>
/// Disposable test harness bundle for <see cref="SharedUseDispatchContractTests"/>
/// implementations: the dispatch under test behind both of its ports, the stores it reads and
/// writes — so the suite can seed orders, targets and occupancy the way production code does and
/// look at what a dispatch left — and a way to read the notifications it staged.
/// </summary>
/// <param name="Service">The dispatch under test, write side.</param>
/// <param name="Plans">The dispatch under test, read side.</param>
/// <param name="Orders">The order store.</param>
/// <param name="TargetReader">The target store, read side.</param>
/// <param name="TargetWriter">The target store, write side.</param>
/// <param name="Occupancy">The occupancy store, scoped to <paramref name="OwnerOrganizationId"/>.</param>
/// <param name="OrderTypes">The registry the dispatch resolves order types from; the suite registers its types here.</param>
/// <param name="Clock">The clock the dispatch reads.</param>
/// <param name="OwnerOrganizationId">The pole owner every store is scoped to.</param>
/// <param name="StagedNotificationsAsync">Every notification staged so far, in the order staged.</param>
/// <param name="Scope">Whatever must be disposed with the harness (a database, a connection).</param>
public sealed record SharedUseDispatchContractHarness(
    ISharedUseDispatchService Service,
    ISharedUseDispatchPlanReader Plans,
    IServiceOrderRepository Orders,
    IServiceOrderTargetReader TargetReader,
    IServiceOrderTargetWriter TargetWriter,
    IPoleOccupancyWriter Occupancy,
    OrderTypeRegistry OrderTypes,
    FakeTimeProvider Clock,
    Guid OwnerOrganizationId,
    Func<CancellationToken, Task<IReadOnlyList<SharedUseStagedNotification>>> StagedNotificationsAsync,
    IAsyncDisposable? Scope = null) : IAsyncDisposable
{
    public ValueTask DisposeAsync() => Scope?.DisposeAsync() ?? ValueTask.CompletedTask;
}
