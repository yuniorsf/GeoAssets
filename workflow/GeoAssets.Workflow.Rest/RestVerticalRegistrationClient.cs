using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GeoAssets.Identity.Authentication;
using GeoAssets.Identity.Authorization.Modules;
using GeoAssets.Workflow.Registry;

namespace GeoAssets.Workflow.Rest;

/// <summary>
/// HTTP implementation of <see cref="IVerticalRegistrationClient"/> relative to <paramref name="http"/>'s base address —
/// the kernel's root, e.g. <c>http://geoassets-server:8080/</c>. Every request carries a fresh service-principal bearer
/// token from <paramref name="tokenProvider"/> when one is registered; without one the kernel answers 401 and the call
/// fails, which a registration loop reports and retries.
/// </summary>
public sealed class RestVerticalRegistrationClient(HttpClient http, IServiceAuthTokenProvider? tokenProvider = null) : IVerticalRegistrationClient
{
    private static readonly JsonSerializerOptions _opts = new(JsonSerializerDefaults.Web);

    public Task PublishOrderTypesAsync(string verticalId, IReadOnlyList<OrderTypeDescriptor> descriptors, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(verticalId);
        ArgumentNullException.ThrowIfNull(descriptors);

        return PutAsync($"api/registry/verticals/{Uri.EscapeDataString(verticalId)}/order-types", descriptors, ct);
    }

    public Task PublishPermissionsAsync(string verticalId, ModulePermissionSet permissions, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(verticalId);
        ArgumentNullException.ThrowIfNull(permissions);

        return PutAsync($"api/identity/verticals/{Uri.EscapeDataString(verticalId)}/permissions", permissions, ct);
    }

    private async Task PutAsync<T>(string path, T body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, new Uri(path, UriKind.Relative))
        {
            Content = JsonContent.Create(body, options: _opts),
        };

        if (tokenProvider is not null)
        {
            var token = await tokenProvider.GetAccessTokenAsync(ct).ConfigureAwait(false);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }
}
