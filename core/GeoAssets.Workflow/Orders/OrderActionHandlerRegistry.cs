namespace GeoAssets.Workflow.Orders;

/// <summary>
/// The registered <see cref="IOrderActionHandler"/>s (XD01-360), in registration order: <see cref="Find"/> returns the first
/// that claims an action; <see cref="EnsureUnambiguous"/> fails fast when two claim the same (order type, action) pair.
/// </summary>
public sealed class OrderActionHandlerRegistry(IEnumerable<IOrderActionHandler> handlers)
{
    private readonly IReadOnlyList<IOrderActionHandler> _handlers = [.. handlers ?? throw new ArgumentNullException(nameof(handlers))];

    /// <summary>The handler that owns <paramref name="entry"/> on an order of <paramref name="orderType"/>, or <see langword="null"/> for the default append.</summary>
    public IOrderActionHandler? Find(OrderType? orderType, OrderActionLog entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return _handlers.FirstOrDefault(handler => handler.CanHandle(orderType, entry));
    }

    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> naming the order type, the action and both handlers when more than one
    /// handler claims the same action on one of <paramref name="orderTypes"/> — every action the type can be asked for: the
    /// generic verbs, every historical action number, and the actions its transitions, permissions and recipients name.
    /// </summary>
    public void EnsureUnambiguous(IEnumerable<OrderType> orderTypes)
    {
        ArgumentNullException.ThrowIfNull(orderTypes);

        if (_handlers.Count < 2)
        {
            return;
        }

        foreach (var orderType in orderTypes)
        {
            foreach (var action in ActionsOf(orderType))
            {
                var probe = new OrderActionLog(action, string.Empty, DateTime.UnixEpoch);
                var claimants = _handlers.Where(handler => handler.CanHandle(orderType, probe)).Take(2).ToArray();
                if (claimants.Length > 1)
                {
                    throw new InvalidOperationException(
                        $"Order action handlers '{claimants[0].GetType().FullName}' and '{claimants[1].GetType().FullName}' both claim action '{action}' on order type '{orderType.Id}'.");
                }
            }
        }
    }

    /// <summary>The actions <paramref name="orderType"/> can be asked for (XD01-366: the vocabulary is open, so it is derived).</summary>
    private static IEnumerable<OrderAction> ActionsOf(OrderType orderType) =>
        OrderAction.Generic
            .Concat(OrderActionAliases.All.Select(a => a.Action))
            .Concat(orderType.Transitions.Where(t => t.TriggerAction is not null).Select(t => t.TriggerAction!.Value))
            .Concat(orderType.ActionPermissions.Select(p => p.Action))
            .Concat(orderType.RecipientActions)
            .Distinct();
}
