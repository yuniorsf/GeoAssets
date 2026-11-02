using System.Net.Http.Json;
using System.Text.Json;
using GeoAssets.Core.Services;
using GeoAssets.Workflow.Orders;

namespace GeoAssets.Shared.Services.Offline;

/// <summary>
/// <see cref="IFieldServiceSyncClient"/> backed by <c>GeoAssets.FieldService.Server</c>'s
/// <c>/api/field-service/sync/service-orders</c> pull/push endpoints (XD01-208). Mirrors
/// <c>RestServiceOrderRepository</c>'s shape (direct, non-caching, relative paths against an
/// <see cref="HttpClient"/> whose <see cref="HttpClient.BaseAddress"/> the caller configures).
/// </summary>
public sealed class RestFieldServiceSyncClient(HttpClient http) : IFieldServiceSyncClient
{
    private static readonly JsonSerializerOptions _opts = GeoJsonSerializer.Options;

    public async Task<IReadOnlyList<ServiceOrder>> PullSinceAsync(DateTime sinceUtc, CancellationToken ct = default)
    {
        var path = $"service-orders?sinceUtc={Uri.EscapeDataString(sinceUtc.ToString("O"))}";
        var orders = await http.GetFromJsonAsync<List<ServiceOrder>>(path, _opts, ct).ConfigureAwait(false);
        return orders ?? [];
    }

    public async Task<IReadOnlyList<RemoteSyncPushResult>> PushAsync(IReadOnlyList<PushServiceOrderItem> items, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync("service-orders", items, _opts, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var results = await response.Content.ReadFromJsonAsync<List<RemoteSyncPushResult>>(_opts, ct).ConfigureAwait(false);
        return results ?? [];
    }
}
