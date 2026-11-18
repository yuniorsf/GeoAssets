namespace GeoAssets.Workflow.Orders;

/// <summary>
/// Takes over the write of an order action the generic <c>POST /service-orders/{id}/actions</c> route would otherwise
/// append (XD01-360, ADR-001 smell B): a module contributes one for the (order type, action) pairs whose effect is more
/// than a status change. It runs only after the rule chain allowed the action, and owns the whole write — the default
/// append is skipped. At most one handler may claim a given pair (checked at startup).
/// </summary>
public interface IOrderActionHandler
{
    /// <summary>True when this handler owns <paramref name="entry"/> on an order of <paramref name="orderType"/>.</summary>
    bool CanHandle(OrderType? orderType, OrderActionLog entry);

    /// <summary>Performs the action. Expected failures are returned as an <see cref="OrderActionOutcome"/>, not thrown.</summary>
    Task<OrderActionOutcome> HandleAsync(OrderActionContext context, CancellationToken cancellationToken);
}
