using GeoAssets.NeutralOperator.Domain.Occupancy;
using Microsoft.Extensions.Time.Testing;

namespace GeoAssets.Workflow.TestKit;

/// <summary>
/// Disposable test harness bundle for <see cref="PoleOccupancyStoreContractTests"/>
/// implementations: the store under test, the clock it stamps writes with, and a way to reach
/// the same stored data as another organization.
/// </summary>
/// <param name="Reader">The store under test, read side, scoped to <paramref name="OrganizationId"/>.</param>
/// <param name="Writer">The store under test, write side, scoped to <paramref name="OrganizationId"/>.</param>
/// <param name="OrganizationId">The pole owner the store under test is scoped to.</param>
/// <param name="Clock">The clock <paramref name="Writer"/> reads; the suite advances it.</param>
/// <param name="AsOrganization">The same underlying store, scoped to the given organization instead.</param>
/// <param name="Scope">Whatever must be disposed with the harness (a database, a connection).</param>
public sealed record PoleOccupancyStoreContractHarness(
    IPoleOccupancyReader Reader,
    IPoleOccupancyWriter Writer,
    Guid OrganizationId,
    FakeTimeProvider Clock,
    Func<Guid, PoleOccupancyStoreView> AsOrganization,
    IAsyncDisposable? Scope = null) : IAsyncDisposable
{
    public ValueTask DisposeAsync() => Scope?.DisposeAsync() ?? ValueTask.CompletedTask;
}
