namespace GeoAssets.Workflow.Orders;

/// <summary>
/// Write surface over <see cref="IServiceOrder"/>, plus the events writes raise.
/// Segregated from <see cref="IServiceOrderReader"/> so a consumer that only ever
/// records changes (a command handler, an ingestion pipeline) can depend on this
/// alone, without needing to know about queries.
/// <see cref="IServiceOrderRepository"/> composes both for the common case of
/// needing full read/write access.
/// </summary>
public interface IServiceOrderWriter
{
    Task AddAsync(IServiceOrder order, CancellationToken ct = default);

    /// <summary>
    /// Persists scalar order fields (title, status, priority, assignee, schedule,
    /// attributes, features, hierarchy) from <paramref name="order"/>.
    /// Does <b>not</b> persist <see cref="IServiceOrder.Dispatches"/> or
    /// <see cref="IServiceOrder.ActionLog"/> — use <see cref="AppendDispatchAsync"/>
    /// and <see cref="AppendActionAsync"/> for those, so concurrent appends from
    /// different callers never race to infer "what's new" from list state.
    /// Throws <see cref="KeyNotFoundException"/> if no order with this ID was
    /// previously added, or <see cref="ServiceOrderConcurrencyException"/> if another
    /// writer committed a change to the same order since it was last read
    /// (only <c>EFServiceOrderRepository</c> currently detects this).
    /// </summary>
    Task UpdateAsync(IServiceOrder order, CancellationToken ct = default);

    /// <summary>
    /// Appends a single dispatch event to the order, independent of any other
    /// concurrent write. Throws <see cref="KeyNotFoundException"/> if the order
    /// does not exist.
    /// </summary>
    Task AppendDispatchAsync(string orderId, OrderDispatch dispatch, CancellationToken ct = default);

    /// <summary>
    /// Appends a single audit-log entry to the order, independent of any other
    /// concurrent write. When <paramref name="entry"/> carries a
    /// <see cref="OrderActionLog.ResultingStatus"/>, the order's status (and
    /// <see cref="IServiceOrder.CompletedAt"/>, when transitioning to
    /// <see cref="ServiceOrderStatus.Completed"/>) is updated atomically with the
    /// log entry. Throws <see cref="KeyNotFoundException"/> if the order does not exist.
    /// </summary>
    Task AppendActionAsync(string orderId, OrderActionLog entry, CancellationToken ct = default);

    Task DeleteAsync(string id, CancellationToken ct = default);

    // ── Events ────────────────────────────────────────────────────────────────

    event EventHandler<IServiceOrder>? OrderAdded;
    event EventHandler<IServiceOrder>? OrderUpdated;
    event EventHandler<(IServiceOrder Order, ServiceOrderStatus Previous)>? OrderStatusChanged;
    event EventHandler<string>?        OrderDeleted;
}
