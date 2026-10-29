using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Services;

namespace GeoAssets.Provider.Rest;

/// <summary>
/// Stateless HTTP implementation of <see cref="IGeoCoreClient"/> backed by
/// <c>GeoAssets.Server</c>'s <c>/api/geoassets</c> endpoints.
/// </summary>
public sealed class RestGeoCoreClient(HttpClient http) : IGeoCoreClient
{
    private static readonly JsonSerializerOptions _opts = GeoJsonSerializer.Options;

    public async Task<GeoFeature?> GetFeatureByIdAsync(string id, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var response = await http.GetAsync(new Uri($"features/{Uri.EscapeDataString(id)}", UriKind.Relative), ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        await EnsureSuccessAsync(response).ConfigureAwait(false);
        return await response.Content.ReadFromJsonAsync<GeoFeature>(_opts, ct).ConfigureAwait(false);
    }

    public async Task<PagedResult<GeoFeature>> QueryFeaturesAsync(AssetQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var response = await http.PostAsJsonAsync("features/query", query, _opts, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response).ConfigureAwait(false);
        return await response.Content.ReadFromJsonAsync<PagedResult<GeoFeature>>(_opts, ct).ConfigureAwait(false)
            ?? new PagedResult<GeoFeature> { Items = [], TotalCount = 0 };
    }

    public async Task<IReadOnlyList<string>> GetNeighborIdsAsync(
        string featureId,
        string? edgeKind = null,
        string? targetAssetTypeId = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(featureId);

        var queryParts = new List<string> { "idsOnly=true" };
        if (!string.IsNullOrWhiteSpace(edgeKind))
            queryParts.Add($"kind={Uri.EscapeDataString(edgeKind)}");
        if (!string.IsNullOrWhiteSpace(targetAssetTypeId))
            queryParts.Add($"targetAssetTypeId={Uri.EscapeDataString(targetAssetTypeId)}");

        var relativePath = $"features/{Uri.EscapeDataString(featureId)}/topology/neighbors?{string.Join("&", queryParts)}";
        var response = await http.GetAsync(new Uri(relativePath, UriKind.Relative), ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) return [];
        await EnsureSuccessAsync(response).ConfigureAwait(false);

        return await response.Content.ReadFromJsonAsync<List<string>>(_opts, ct).ConfigureAwait(false) ?? [];
    }

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> GetNeighborIdsBatchAsync(
        IReadOnlyList<string> featureIds,
        string? edgeKind = null,
        string? targetAssetTypeId = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(featureIds);
        if (featureIds.Count == 0)
            return new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

        var request = new TopologyNeighborsBatchRequest
        {
            FeatureIds        = featureIds,
            Kind              = edgeKind,
            TargetAssetTypeId = targetAssetTypeId
        };

        var response = await http.PostAsJsonAsync("features/topology/neighbors-batch", request, _opts, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response).ConfigureAwait(false);

        var raw = await response.Content.ReadFromJsonAsync<Dictionary<string, List<string>>>(_opts, ct).ConfigureAwait(false);
        if (raw is null)
            return new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

        return raw.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<string>)kv.Value,
            StringComparer.Ordinal);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            var body = await response.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
            var reason = body.ValueKind == JsonValueKind.Object && body.TryGetProperty("reason", out var reasonEl)
                ? reasonEl.GetString() ?? "Not authorized."
                : "Not authorized.";
            throw new UnauthorizedAccessException(reason);
        }

        response.EnsureSuccessStatusCode();
    }
}

