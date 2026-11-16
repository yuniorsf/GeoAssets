using System.Net.Http.Json;
using System.Text.Json;

namespace GeoAssets.Web.Services.IpBilling.Rest;

/// <summary>
/// Turns a <c>GeoAssets.IpBilling.Server</c> 4xx response carrying a <c>{ "message": … }</c>
/// body into an exception with that message, so the UI shows the server's own explanation
/// (e.g. "lamp VS 70 W is not in the chart", XD01-301) rather than a bare status code.
/// </summary>
internal static class RestErrors
{
    private sealed record MessageBody(string? Message);

    public static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;

        var status = (int)response.StatusCode;
        if (status is >= 400 and < 500)
        {
            var message = await TryReadMessageAsync(response, ct);
            if (!string.IsNullOrWhiteSpace(message)) throw new InvalidOperationException(message);
        }

        response.EnsureSuccessStatusCode();
    }

    private static async Task<string?> TryReadMessageAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var body = await response.Content.ReadFromJsonAsync<MessageBody>(ct);
            return body?.Message;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
