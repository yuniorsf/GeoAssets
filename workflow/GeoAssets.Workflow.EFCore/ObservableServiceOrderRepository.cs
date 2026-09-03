using GeoAssets.Infrastructure.Observability;
using GeoAssets.Workflow.Orders;
using Microsoft.Extensions.Logging;

namespace GeoAssets.Workflow.Persistence;

/// <summary>
/// Decorates any <see cref="IServiceOrderRepository"/> implementation with tracing, metrics,
/// and structured logging for status transitions.
///
/// Subscribes to the inner repository's <see cref="IServiceOrderRepository.OrderStatusChanged"/>
/// event to record a <see cref="GeoAssetsActivitySource.StartOrderActivity"/> span and a
/// <see cref="GeoAssetsMeter.RecordOrderTransition"/> metric for every transition that actually
/// happens — reusing the event both <c>EFServiceOrderRepository</c> and
/// <c>RestServiceOrderRepository</c> already raise (only when the status genuinely changes)
/// rather than duplicating that check here. <see cref="UpdateAsync"/>/<see cref="AppendActionAsync"/> are
/// additionally wrapped to log rejected transitions (<see cref="InvalidServiceOrderTransitionException"/>),
/// which never reach <c>OrderStatusChanged</c> since the order was never actually updated.
///
/// Wrap outermost — around <see cref="ValidatingServiceOrderRepository"/>, not inside it — so a
/// transition <see cref="ValidatingServiceOrderRepository"/> itself rejects is still logged.
/// <c>WorkflowEFCoreServiceExtensions.AddWorkflowPersistence</c> registers it this way by default.
/// Requires <see cref="GeoAssetsActivitySource"/>/<see cref="GeoAssetsMeter"/> to already be
/// registered — the host must call <c>AddGeoAssetsObservability</c> before
/// <c>AddWorkflowPersistence</c>, the same requirement <c>KafkaOrderEventPublisher</c>/
/// <c>ServiceBusOrderEventPublisher</c> already impose (XD01-32).
///
/// Not applied to <c>AddWorkflowRest</c> (the Blazor WASM registration in
/// <c>GeoAssets.Web/Program.cs</c>): <c>GeoAssets.Infrastructure.Observability</c> carries an
/// ASP.NET Core <c>FrameworkReference</c> that a WASM client project can't take on — the same
/// constraint documented on <c>ImportDiagnostics</c>, which exists specifically to be usable from
/// both sides of that boundary.
/// </summary>
public sealed class ObservableServiceOrderRepository : IServiceOrderRepository
{
    private readonly IServiceOrderRepository _inner;
    private readonly GeoAssetsActivitySource _tracer;
    private readonly GeoAssetsMeter _metrics;
    private readonly ILogger<ObservableServiceOrderRepository> _logger;

    public ObservableServiceOrderRepository(
        IServiceOrderRepository inner,
        GeoAssetsActivitySource tracer,
        GeoAssetsMeter metrics,
        ILogger<ObservableServiceOrderRepository> logger)
    {
        _inner   = inner;
        _tracer  = tracer;
        _metrics = metrics;
        _logger  = logger;

        _inner.OrderStatusChanged += OnOrderStatusChanged;
    }

    private void OnOrderStatusChanged(object? sender, (IServiceOrder Order, string Previous) e)
    {
        using var span = _tracer.StartOrderActivity("Transition", e.Order.Id)
            ?.AddTag("order.type", e.Order.OrderTypeId)
             .AddTag("order.prev_status", e.Previous)
             .AddTag("order.new_status", e.Order.Status);

        _metrics.RecordOrderTransition(e.Order.OrderTypeId, e.Previous, e.Order.Status);
    }

    // ── Read / query pass-through ────────────────────────────────────────────

    public Task<IServiceOrder?> GetByIdAsync(string id, CancellationToken ct = default)
        => _inner.GetByIdAsync(id, ct);

    public Task<IReadOnlyList<IServiceOrder>> GetAllAsync(CancellationToken ct = default)
        => _inner.GetAllAsync(ct);

    public Task<IReadOnlyList<IServiceOrder>> GetRootsAsync(CancellationToken ct = default)
        => _inner.GetRootsAsync(ct);

    public Task<IReadOnlyList<IServiceOrder>> GetChildrenAsync(string parentId, CancellationToken ct = default)
        => _inner.GetChildrenAsync(parentId, ct);

    public Task<IServiceOrder?> GetParentAsync(string childId, CancellationToken ct = default)
        => _inner.GetParentAsync(childId, ct);

    public Task<IReadOnlyList<IServiceOrder>> GetByStatusAsync(string status, CancellationToken ct = default)
        => _inner.GetByStatusAsync(status, ct);

    public Task<IReadOnlyList<IServiceOrder>> GetByAssigneeAsync(string userId, CancellationToken ct = default)
        => _inner.GetByAssigneeAsync(userId, ct);

    public Task<IReadOnlyList<IServiceOrder>> GetByCreatorAsync(string userId, CancellationToken ct = default)
        => _inner.GetByCreatorAsync(userId, ct);

    public Task<IReadOnlyList<IServiceOrder>> GetByOrderTypeAsync(string orderTypeId, CancellationToken ct = default)
        => _inner.GetByOrderTypeAsync(orderTypeId, ct);

    public Task<IReadOnlyList<IServiceOrder>> GetByDateRangeAsync(DateTime from, DateTime to, CancellationToken ct = default)
        => _inner.GetByDateRangeAsync(from, to, ct);

    public Task<IReadOnlyList<IServiceOrder>> GetDispatchedToAsync(
        string targetId, DispatchTargetType targetType, CancellationToken ct = default)
        => _inner.GetDispatchedToAsync(targetId, targetType, ct);

    // ── Write ─────────────────────────────────────────────────────────────────

    public Task AddAsync(IServiceOrder order, CancellationToken ct = default)
        => _inner.AddAsync(order, ct);

    public async Task UpdateAsync(IServiceOrder order, CancellationToken ct = default)
    {
        try
        {
            await _inner.UpdateAsync(order, ct);
        }
        catch (InvalidServiceOrderTransitionException ex)
        {
            LogRejectedTransition(order.Id, ex);
            throw;
        }
    }

    public Task AppendDispatchAsync(string orderId, OrderDispatch dispatch, CancellationToken ct = default)
        => _inner.AppendDispatchAsync(orderId, dispatch, ct);

    public async Task AppendActionAsync(string orderId, OrderActionLog entry, CancellationToken ct = default)
    {
        try
        {
            await _inner.AppendActionAsync(orderId, entry, ct);
        }
        catch (InvalidServiceOrderTransitionException ex)
        {
            LogRejectedTransition(orderId, ex);
            throw;
        }
    }

    public Task DeleteAsync(string id, CancellationToken ct = default)
        => _inner.DeleteAsync(id, ct);

    private void LogRejectedTransition(string orderId, InvalidServiceOrderTransitionException ex) =>
        _logger.LogWarning(ex,
            "Rejected invalid ServiceOrder transition for order {OrderId}: {From} -> {To}",
            orderId, ex.From, ex.To);

    // ── Events (forwarded) ───────────────────────────────────────────────────

    public event EventHandler<IServiceOrder>? OrderAdded
    {
        add    => _inner.OrderAdded += value;
        remove => _inner.OrderAdded -= value;
    }

    public event EventHandler<IServiceOrder>? OrderUpdated
    {
        add    => _inner.OrderUpdated += value;
        remove => _inner.OrderUpdated -= value;
    }

    public event EventHandler<(IServiceOrder Order, string Previous)>? OrderStatusChanged
    {
        add    => _inner.OrderStatusChanged += value;
        remove => _inner.OrderStatusChanged -= value;
    }

    public event EventHandler<string>? OrderDeleted
    {
        add    => _inner.OrderDeleted += value;
        remove => _inner.OrderDeleted -= value;
    }
}
