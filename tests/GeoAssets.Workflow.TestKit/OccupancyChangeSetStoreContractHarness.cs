using GeoAssets.NeutralOperator.Domain.Occupancy;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Targets;
using Microsoft.Extensions.Time.Testing;

namespace GeoAssets.Workflow.TestKit;

/// <summary>
/// Disposable test harness bundle for <see cref="OccupancyChangeSetStoreContractTests"/>
/// implementations: the store under test, the order and target stores it sits beside — so the
/// suite can create orders and targets, remove a target and close or delete an order the way
/// production code does — the clock, and a way to reach the same data as another organization.
/// </summary>
/// <param name="Reader">The store under test, read side, scoped to <paramref name="OrganizationId"/>.</param>
/// <param name="Writer">The store under test, write side, scoped to <paramref name="OrganizationId"/>.</param>
/// <param name="OrganizationId">The pole owner the store under test is scoped to.</param>
/// <param name="Clock">The clock the store, and whatever closes orders, reads.</param>
/// <param name="Orders">The order store the targets belong to.</param>
/// <param name="Targets">The target store change sets are staged against.</param>
/// <param name="AsOrganization">The same underlying store, scoped to the given organization instead.</param>
/// <param name="Scope">Whatever must be disposed with the harness (a database, a connection).</param>
public sealed record OccupancyChangeSetStoreContractHarness(
    IOccupancyChangeSetReader Reader,
    IOccupancyChangeSetWriter Writer,
    Guid OrganizationId,
    FakeTimeProvider Clock,
    IServiceOrderRepository Orders,
    IServiceOrderTargetWriter Targets,
    Func<Guid, OccupancyChangeSetStoreView> AsOrganization,
    IAsyncDisposable? Scope = null) : IAsyncDisposable
{
    public ValueTask DisposeAsync() => Scope?.DisposeAsync() ?? ValueTask.CompletedTask;
}
