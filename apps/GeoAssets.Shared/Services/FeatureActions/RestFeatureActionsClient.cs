using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GeoAssets.Workflow.Registry;

namespace GeoAssets.Shared.Services.FeatureActions;

/// <summary>
/// <see cref="IFeatureActionsClient"/> over the GeoAssets Server REST API (XD01-370). <paramref name="http"/>'s base
/// address is the server's <c>/api/geoassets/</c>. A feature the server does not find (404) has no actions; any other
/// failure throws, so the caller can tell "none" from "unavailable".
/// </summary>
public sealed class RestFeatureActionsClient(HttpClient http) : IFeatureActionsClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<FeatureActionDescriptor>> GetActionsAsync(string featureId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(featureId);

        using var response = await http
            .GetAsync(new Uri($"features/{Uri.EscapeDataString(featureId)}/actions", UriKind.Relative), ct)
            .ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) return [];

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<FeatureActionDescriptor[]>(Json, ct).ConfigureAwait(false) ?? [];
    }
}
