using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GeoAssets.Core.Services;
using GeoAssets.Workflow.Orders;

namespace GeoAssets.Workflow.Rest;

/// <summary>
/// <see cref="IServiceOrderRepository"/> backed by a remote GeoAssets Workflow REST API
/// (see <c>GeoAssets.Server</c>'s <c>ServiceOrdersRestApiExtensions.MapServiceOrdersApi</c>).
///
/// Unlike <c>RestAssetProvider</c>, this is a direct, non-caching client: every method awaits
/// its own HTTP round trip and returns/throws exactly what the server reports. A local cache +
/// fire-and-forget writes (the asset pattern) isn't appropriate here — <see cref="IServiceOrderWriter"/>
/// is fully async and callers are documented to expect <see cref="ServiceOrderConcurrencyException"/>/
/// <see cref="KeyNotFoundException"/>/<see cref="InvalidServiceOrderTransitionException"/> to
/// propagate from writes, which a fire-and-forget model would silently swallow.
///
/// Events are raised locally after a successful write completes (there is no server push).
/// <see cref="UpdateAsync"/> and <see cref="AppendDispatchAsync"/> never fire
/// <see cref="OrderStatusChanged"/> — the actual UI only changes status through
/// <see cref="AppendActionAsync"/> (see <c>ServiceOrderDetail.razor</c>'s <c>RecordAction</c>),
/// so detecting a status change on those two paths would cost an extra round trip for an event
/// no caller observes. <see cref="AppendActionAsync"/> reads the order's status before and after
/// specifically to support this event, mirroring <c>EFServiceOrderRepository</c>'s own
/// read-before/read-after shape.
/// </summary>
public sealed class RestServiceOrderRepository(HttpClient http) : IServiceOrderRepository
{
    private static readonly JsonSerializerOptions _opts = GeoJsonSerializer.GetOptions();

    public event EventHandler<IServiceOrder>? OrderAdded;
    public event EventHandler<IServiceOrder>? OrderUpdated;
    public event EventHandler<(IServiceOrder Order, string Previous)>? OrderStatusChanged;
    public event EventHandler<string>? OrderDeleted;

    // ── Read ──────────────────────────────────────────────────────────────────

    public async Task<IServiceOrder?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        var response = await http.GetAsync($"service-orders/{Uri.EscapeDataString(id)}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ServiceOrder>(_opts, ct);
    }

    public Task<IReadOnlyList<IServiceOrder>> GetAllAsync(CancellationToken ct = default)
        => GetOrdersAsync("service-orders", ct);

    public Task<IReadOnlyList<IServiceOrder>> GetRootsAsync(CancellationToken ct = default)
        => GetOrdersAsync("service-orders/roots", ct);

    public Task<IReadOnlyList<IServiceOrder>> GetChildrenAsync(string parentId, CancellationToken ct = default)
        => GetOrdersAsync($"service-orders/{Uri.EscapeDataString(parentId)}/children", ct);

    public async Task<IServiceOrder?> GetParentAsync(string childId, CancellationToken ct = default)
    {
        var response = await http.GetAsync($"service-orders/{Uri.EscapeDataString(childId)}/parent", ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ServiceOrder>(_opts, ct);
    }

    public Task<IReadOnlyList<IServiceOrder>> GetByStatusAsync(string status, CancellationToken ct = default)
        => GetOrdersAsync($"service-orders/by-status/{Uri.EscapeDataString(status)}", ct);

    public Task<IReadOnlyList<IServiceOrder>> GetByAssigneeAsync(string userId, CancellationToken ct = default)
        => GetOrdersAsync($"service-orders/by-assignee/{Uri.EscapeDataString(userId)}", ct);

    public Task<IReadOnlyList<IServiceOrder>> GetByCreatorAsync(string userId, CancellationToken ct = default)
        => GetOrdersAsync($"service-orders/by-creator/{Uri.EscapeDataString(userId)}", ct);

    public Task<IReadOnlyList<IServiceOrder>> GetByOrderTypeAsync(string orderTypeId, CancellationToken ct = default)
        => GetOrdersAsync($"service-orders/by-order-type/{Uri.EscapeDataString(orderTypeId)}", ct);

    public Task<IReadOnlyList<IServiceOrder>> GetByDateRangeAsync(DateTime from, DateTime to, CancellationToken ct = default)
        => GetOrdersAsync(
            $"service-orders/by-date-range?from={Uri.EscapeDataString(from.ToString("O"))}&to={Uri.EscapeDataString(to.ToString("O"))}",
            ct);

    public Task<IReadOnlyList<IServiceOrder>> GetDispatchedToAsync(
        string targetId, DispatchTargetType targetType, CancellationToken ct = default)
        => GetOrdersAsync(
            $"service-orders/dispatched-to/{Uri.EscapeDataString(targetId)}?targetType={targetType}",
            ct);

    private async Task<IReadOnlyList<IServiceOrder>> GetOrdersAsync(string path, CancellationToken ct)
        => await http.GetFromJsonAsync<ServiceOrder[]>(path, _opts, ct) ?? [];

    // ── Write ─────────────────────────────────────────────────────────────────

    public async Task AddAsync(IServiceOrder order, CancellationToken ct = default)
    {
        var so = AsServiceOrder(order);
        var response = await http.PostAsJsonAsync("service-orders", so, _opts, ct);
        await EnsureWriteSuccessAsync(response, so.Id);

        OrderAdded?.Invoke(this, order);
    }

    public async Task UpdateAsync(IServiceOrder order, CancellationToken ct = default)
    {
        var so = AsServiceOrder(order);
        var response = await http.PutAsJsonAsync($"service-orders/{Uri.EscapeDataString(so.Id)}", so, _opts, ct);
        await EnsureWriteSuccessAsync(response, so.Id);

        OrderUpdated?.Invoke(this, order);
    }

    public async Task AppendDispatchAsync(string orderId, OrderDispatch dispatch, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync($"service-orders/{Uri.EscapeDataString(orderId)}/dispatch", dispatch, _opts, ct);
        await EnsureWriteSuccessAsync(response, orderId);

        var updated = await GetByIdAsync(orderId, ct)
            ?? throw new KeyNotFoundException($"ServiceOrder '{orderId}' not found.");
        OrderUpdated?.Invoke(this, updated);
    }

    public async Task AppendActionAsync(string orderId, OrderActionLog entry, CancellationToken ct = default)
    {
        string? previous = null;
        if (entry.ResultingStatus is not null)
        {
            var existing = await GetByIdAsync(orderId, ct)
                ?? throw new KeyNotFoundException($"ServiceOrder '{orderId}' not found.");
            previous = existing.Status;
        }

        var response = await http.PostAsJsonAsync($"service-orders/{Uri.EscapeDataString(orderId)}/actions", entry, _opts, ct);
        await EnsureWriteSuccessAsync(response, orderId);

        var updated = await GetByIdAsync(orderId, ct)
            ?? throw new KeyNotFoundException($"ServiceOrder '{orderId}' not found.");
        OrderUpdated?.Invoke(this, updated);

        if (previous is not null && previous != updated.Status)
            OrderStatusChanged?.Invoke(this, (updated, previous));
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        var response = await http.DeleteAsync($"service-orders/{Uri.EscapeDataString(id)}", ct);
        response.EnsureSuccessStatusCode();

        OrderDeleted?.Invoke(this, id);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ServiceOrder AsServiceOrder(IServiceOrder order) =>
        order as ServiceOrder ?? throw new ArgumentException($"Expected {nameof(ServiceOrder)}.", nameof(order));

    /// <summary>
    /// Translates a failed write response into the same domain exceptions
    /// <c>EFServiceOrderRepository</c>/<c>ValidatingServiceOrderRepository</c> throw server-side,
    /// so callers observe identical exception types regardless of backend. See
    /// <c>ServiceOrdersRestApiExtensions</c> for the matching response shapes.
    /// </summary>
    private static async Task EnsureWriteSuccessAsync(HttpResponseMessage response, string orderId)
    {
        if (response.IsSuccessStatusCode) return;

        switch (response.StatusCode)
        {
            case HttpStatusCode.NotFound:
                throw new KeyNotFoundException($"ServiceOrder '{orderId}' not found.");

            case HttpStatusCode.Conflict:
                throw new ServiceOrderConcurrencyException(orderId);

            case HttpStatusCode.BadRequest:
                var body = await response.Content.ReadFromJsonAsync<JsonElement>();

                if (body.TryGetProperty("errors", out var errorsEl))
                {
                    var orderTypeId = body.TryGetProperty("orderTypeId", out var idEl) ? idEl.GetString() ?? "" : "";
                    var errors = errorsEl.EnumerateArray().Select(e => e.GetString() ?? "").ToList();
                    throw new ServiceOrderAttributeValidationException(orderTypeId, errors);
                }

                if (body.TryGetProperty("from", out var fromEl) && body.TryGetProperty("to", out var toEl))
                    throw new InvalidServiceOrderTransitionException(fromEl.GetString() ?? "", toEl.GetString() ?? "");

                response.EnsureSuccessStatusCode();
                break;

            default:
                response.EnsureSuccessStatusCode();
                break;
        }
    }
}
