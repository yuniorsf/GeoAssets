namespace GeoAssets.Workflow.Orders;

/// <summary>
/// Thrown by <see cref="IServiceOrderWriter.UpdateAsync"/> when another writer committed
/// a change to the same order between this call's read and its save — detected via the
/// store's optimistic concurrency token (see <c>EFServiceOrderRepository</c>).
/// Only <c>EFServiceOrderRepository</c> currently detects this; other implementations have
/// no equivalent check.
/// </summary>
public sealed class ServiceOrderConcurrencyException(string orderId)
    : InvalidOperationException($"ServiceOrder '{orderId}' was modified by another writer. Reload and retry.")
{
    public string OrderId { get; } = orderId;
}
