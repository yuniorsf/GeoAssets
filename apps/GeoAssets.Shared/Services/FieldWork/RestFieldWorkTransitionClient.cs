using System.Net.Http.Json;
using System.Text.Json;
using GeoAssets.Core.Services;

namespace GeoAssets.Shared.Services.FieldWork;

/// <summary>
/// <see cref="IFieldWorkTransitionClient"/> backed by <c>GeoAssets.FieldService.Server</c>'s
/// <c>/api/field-service/orders/{id}/field-work-transition</c> endpoint (XD01-295). Mirrors
/// <c>RestServiceOrderRepository</c>'s/<c>RestFieldServiceSyncClient</c>'s shape (direct,
/// non-caching, relative paths against an <see cref="HttpClient"/> whose
/// <see cref="HttpClient.BaseAddress"/> the caller configures).
/// </summary>
public sealed class RestFieldWorkTransitionClient(HttpClient http) : IFieldWorkTransitionClient
{
    private static readonly JsonSerializerOptions _opts = GeoJsonSerializer.Options;

    /// <summary>Property names match the server's internal <c>FieldWorkTransitionRequest</c> record exactly, same camelCase round-trip reason as <c>PushServiceOrderItem</c>.</summary>
    private sealed record Request(string NewStatus, byte[] RowVersion, Dictionary<string, string>? Attributes, double? DeviceLongitude, double? DeviceLatitude);

    private sealed record Response(string ServiceOrderId, string Status, byte[] RowVersion);

    public async Task<FieldWorkTransitionOutcome> AttemptTransitionAsync(
        string serviceOrderId,
        string newStatus,
        byte[] rowVersion,
        IReadOnlyDictionary<string, string>? attributes = null,
        double? deviceLongitude = null,
        double? deviceLatitude = null,
        CancellationToken ct = default)
    {
        var request = new Request(
            newStatus,
            rowVersion,
            attributes is null ? null : new Dictionary<string, string>(attributes, StringComparer.Ordinal),
            deviceLongitude,
            deviceLatitude);

        var httpResponse = await http.PostAsJsonAsync($"{serviceOrderId}/field-work-transition", request, _opts, ct).ConfigureAwait(false);
        if (!httpResponse.IsSuccessStatusCode)
            return FieldWorkTransitionOutcome.Rejected(await ExtractReasonAsync(httpResponse, ct).ConfigureAwait(false));

        var body = await httpResponse.Content.ReadFromJsonAsync<Response>(_opts, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Server returned a success status with no response body.");

        return FieldWorkTransitionOutcome.Success(body.Status, body.RowVersion);
    }

    private static async Task<string> ExtractReasonAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var raw = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(raw))
            return $"Request failed with status {(int)response.StatusCode}.";

        try
        {
            return JsonSerializer.Deserialize<string>(raw, _opts) ?? raw;
        }
        catch (JsonException)
        {
            return raw;
        }
    }
}
