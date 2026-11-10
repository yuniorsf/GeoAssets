using GeoAssets.Workflow.FieldWork;
using GeoAssets.Workflow.Orders;

namespace GeoAssets.Workflow.Erp.Sap;

/// <summary>
/// Transitions a SAP PM-spawned order to <see cref="FieldWorkStatus.Inspected"/> and, exactly
/// once, notifies SAP via <see cref="ISapPmConfirmationDispatcher"/> (XD01-231's AC2) —
/// idempotency enforced by <see cref="SapPmOrderAttributes.ConfirmationDispatchedAtKey"/> rather
/// than re-dispatching on a retried transition call. Same transition-then-notify shape as
/// <see cref="Isp.Bss.FtthDropInstallActivationDispatchService"/>: the status transition and its
/// persistence always succeed by the time dispatch is attempted, so a dispatch failure is
/// reported as data, not an exception — there is nothing left to roll back.
/// </summary>
public sealed class SapPmOrderConfirmationDispatchService(
    IServiceOrderRepository serviceOrders,
    ISapPmConfirmationDispatcher dispatcher,
    TimeProvider timeProvider)
{
    public async Task<SapPmConfirmationDispatchResult> TransitionToInspectedAsync(
        string orderId, decimal laborHours, IReadOnlyList<SapPmMaterialLine> materials, string evidenceDossierUrl,
        OrderType orderType, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(orderId);
        ArgumentNullException.ThrowIfNull(materials);
        ArgumentException.ThrowIfNullOrEmpty(evidenceDossierUrl);
        ArgumentNullException.ThrowIfNull(orderType);

        var existing = await serviceOrders.GetByIdAsync(orderId, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"ServiceOrder '{orderId}' not found.");

        if (existing is not ServiceOrder concreteOrder)
            throw new InvalidOperationException($"Expected a {nameof(ServiceOrder)} instance, got {existing.GetType().Name}.");

        if (concreteOrder.Attributes.ContainsKey(SapPmOrderAttributes.ConfirmationDispatchedAtKey))
            return new SapPmConfirmationDispatchResult(concreteOrder, ConfirmationDispatched: false, ConfirmationFailureReason: null);

        if (!string.Equals(concreteOrder.Status, FieldWorkStatus.Inspected, StringComparison.Ordinal))
        {
            concreteOrder.Transition(FieldWorkStatus.Inspected, timeProvider, orderType);
            await serviceOrders.UpdateAsync(concreteOrder, ct).ConfigureAwait(false);
        }

        return await DispatchConfirmationAsync(concreteOrder, laborHours, materials, evidenceDossierUrl, ct).ConfigureAwait(false);
    }

    private async Task<SapPmConfirmationDispatchResult> DispatchConfirmationAsync(
        ServiceOrder order, decimal laborHours, IReadOnlyList<SapPmMaterialLine> materials, string evidenceDossierUrl, CancellationToken ct)
    {
        var orderNumber = order.Attributes.GetValueOrDefault(SapPmOrderAttributes.OrderNumberKey);
        if (string.IsNullOrEmpty(orderNumber))
        {
            return new SapPmConfirmationDispatchResult(
                order, ConfirmationDispatched: false,
                ConfirmationFailureReason: $"Order '{order.Id}' has no recorded {SapPmOrderAttributes.OrderNumberKey} — it was not spawned from a SAP PM notification.");
        }

        var confirmedAtUtc = timeProvider.GetUtcNow();
        var confirmation = new SapPmOrderConfirmation(orderNumber, laborHours, materials, evidenceDossierUrl, confirmedAtUtc);

        try
        {
            await dispatcher.DispatchAsync(confirmation, ct).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            return new SapPmConfirmationDispatchResult(order, ConfirmationDispatched: false, ConfirmationFailureReason: ex.Message);
        }
        catch (HttpRequestException ex)
        {
            return new SapPmConfirmationDispatchResult(order, ConfirmationDispatched: false, ConfirmationFailureReason: ex.Message);
        }

        order.Attributes[SapPmOrderAttributes.EvidenceDossierUrlKey] = evidenceDossierUrl;
        order.Attributes[SapPmOrderAttributes.ConfirmationDispatchedAtKey] = confirmedAtUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        await serviceOrders.UpdateAsync(order, ct).ConfigureAwait(false);

        return new SapPmConfirmationDispatchResult(order, ConfirmationDispatched: true, ConfirmationFailureReason: null);
    }
}
