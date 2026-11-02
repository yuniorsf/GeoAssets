using GeoAssets.Workflow.FieldWork;
using GeoAssets.Workflow.Orders;

namespace GeoAssets.Workflow.Isp.Bss;

/// <summary>
/// Moves a FTTH drop-install order through <see cref="FtthDropInstallWorkflowGraph"/> via
/// <see cref="FtthDropInstallTransitioner"/>, and — when the destination is
/// <see cref="FtthDropInstallStatus.Activated"/> — notifies the BSS/OSS provider that spawned it
/// (XD01-280's AC2), resolved from the order's own <see cref="BssOrderAttributes.ProviderIdKey"/>
/// rather than a separate tracking table.
/// </summary>
public sealed class FtthDropInstallActivationDispatchService(
    IServiceOrderRepository serviceOrders,
    BssOssOrderAdapterRegistry adapterRegistry,
    IBssOssOutboundWebhookDispatcher outboundDispatcher,
    TimeProvider timeProvider)
{
    public async Task<FtthDropInstallTransitionResult> TransitionAsync(
        string orderId, string newStatus, OrderType ftthOrderType, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(orderId);
        ArgumentException.ThrowIfNullOrEmpty(newStatus);
        ArgumentNullException.ThrowIfNull(ftthOrderType);

        var existing = await serviceOrders.GetByIdAsync(orderId, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"ServiceOrder '{orderId}' not found.");

        if (existing is not ServiceOrder concreteOrder)
            throw new InvalidOperationException($"Expected a {nameof(ServiceOrder)} instance, got {existing.GetType().Name}.");

        FtthDropInstallTransitioner.Transition(concreteOrder, newStatus, timeProvider, ftthOrderType);
        await serviceOrders.UpdateAsync(concreteOrder, ct).ConfigureAwait(false);

        if (!string.Equals(newStatus, FtthDropInstallStatus.Activated, StringComparison.Ordinal))
            return new FtthDropInstallTransitionResult(concreteOrder, ActivationWebhookDispatched: false, ActivationWebhookFailureReason: null);

        return await DispatchActivationAsync(concreteOrder, ct).ConfigureAwait(false);
    }

    private async Task<FtthDropInstallTransitionResult> DispatchActivationAsync(ServiceOrder order, CancellationToken ct)
    {
        var providerId = order.Attributes.GetValueOrDefault(BssOrderAttributes.ProviderIdKey);
        if (string.IsNullOrEmpty(providerId) || !adapterRegistry.TryResolve(providerId, out var adapter))
            return new FtthDropInstallTransitionResult(order, ActivationWebhookDispatched: false, ActivationWebhookFailureReason: null);

        try
        {
            var payload = adapter.BuildActivationPayload(order);
            await outboundDispatcher.DispatchAsync(providerId, payload, ct).ConfigureAwait(false);
            return new FtthDropInstallTransitionResult(order, ActivationWebhookDispatched: true, ActivationWebhookFailureReason: null);
        }
        catch (InvalidOperationException ex)
        {
            return new FtthDropInstallTransitionResult(order, ActivationWebhookDispatched: false, ActivationWebhookFailureReason: ex.Message);
        }
        catch (HttpRequestException ex)
        {
            return new FtthDropInstallTransitionResult(order, ActivationWebhookDispatched: false, ActivationWebhookFailureReason: ex.Message);
        }
    }
}
