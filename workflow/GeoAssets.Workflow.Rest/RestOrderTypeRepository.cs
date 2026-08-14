using System.Net;
using System.Net.Http.Json;
using GeoAssets.Core.Services;
using GeoAssets.Workflow.Orders;

namespace GeoAssets.Workflow.Rest;

/// <summary>
/// <see cref="IOrderTypeRepository"/> backed by a remote GeoAssets Workflow REST API.
///
/// Unlike <c>EFOrderTypeRepository</c> (which defers persistence to an explicit
/// <see cref="SaveChangesAsync"/>), each write here (<see cref="AddAsync"/>/<see cref="UpdateAsync"/>/
/// <see cref="DeleteAsync"/>) is already fully persisted by the time its HTTP call returns — the
/// server calls its own <c>SaveChangesAsync</c> internally per request (see
/// <c>ServiceOrdersRestApiExtensions</c>). <see cref="SaveChangesAsync"/> is therefore a no-op here,
/// kept only to satisfy the interface.
/// </summary>
public sealed class RestOrderTypeRepository(HttpClient http) : IOrderTypeRepository
{
    private static readonly System.Text.Json.JsonSerializerOptions _opts = GeoJsonSerializer.GetOptions();

    public async Task<OrderType?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        var response = await http.GetAsync($"order-types/{Uri.EscapeDataString(id)}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<OrderType>(_opts, ct);
    }

    public async Task<IReadOnlyList<OrderType>> GetAllAsync(CancellationToken ct = default)
        => await http.GetFromJsonAsync<OrderType[]>("order-types", _opts, ct) ?? [];

    public async Task AddAsync(OrderType orderType, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync("order-types", orderType, _opts, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task UpdateAsync(OrderType orderType, CancellationToken ct = default)
    {
        var response = await http.PutAsJsonAsync($"order-types/{Uri.EscapeDataString(orderType.Id)}", orderType, _opts, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        var response = await http.DeleteAsync($"order-types/{Uri.EscapeDataString(id)}", ct);
        response.EnsureSuccessStatusCode();
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
}
