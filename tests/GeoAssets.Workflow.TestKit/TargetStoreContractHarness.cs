using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Targets;

namespace GeoAssets.Workflow.TestKit;

/// <summary>
/// Disposable test harness bundle for <see cref="ServiceOrderTargetStoreContractTests"/>
/// implementations: the store under test plus the order store and order-type registry it
/// reads, so the suite can create orders, register their types and close them the same way
/// production code does.
/// </summary>
/// <param name="Reader">The store under test, read side.</param>
/// <param name="Writer">The store under test, write side.</param>
/// <param name="Orders">The order store <paramref name="Reader"/>/<paramref name="Writer"/> resolve orders from.</param>
/// <param name="OrderTypes">The registry the store takes each order type's exclusivity and terminal states from.</param>
/// <param name="TenantId">The organization the store under test is scoped to.</param>
/// <param name="Scope">Whatever must be disposed with the harness (a database, a connection).</param>
public sealed record TargetStoreContractHarness(
    IServiceOrderTargetReader Reader,
    IServiceOrderTargetWriter Writer,
    IServiceOrderRepository Orders,
    OrderTypeRegistry OrderTypes,
    Guid TenantId,
    IAsyncDisposable? Scope = null) : IAsyncDisposable
{
    public ValueTask DisposeAsync() => Scope?.DisposeAsync() ?? ValueTask.CompletedTask;
}
