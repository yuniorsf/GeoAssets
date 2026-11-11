using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Messaging.Events;
using GeoAssets.Core.Models;
using GeoAssets.Core.Services;
using GeoAssets.Identity.Authentication;

namespace GeoAssets.Provider.Rest;

/// <summary>
/// Stateless HTTP implementation of <see cref="IGeoCoreClient"/> backed by
/// <c>GeoAssets.Server</c>'s <c>/api/geoassets</c> endpoints.
///
/// <paramref name="tokenProvider"/> is optional (XD01-299): when supplied, every request carries
/// a fresh <c>Authorization: Bearer</c> header acquired per-call (never cached on the shared
/// <see cref="HttpClient"/>'s <c>DefaultRequestHeaders</c>, which would race across concurrent
/// requests) so this client authenticates itself as a service caller. When omitted, behavior is
/// unchanged from before this ticket — callers relying on network-level trust alone keep working
/// until a real token provider is wired in.
/// </summary>
public sealed class RestGeoCoreClient(HttpClient http, IServiceAuthTokenProvider? tokenProvider = null) : IGeoCoreClient
{
    private static readonly JsonSerializerOptions _opts = GeoJsonSerializer.Options;

    public async Task<GeoFeature?> GetFeatureByIdAsync(string id, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        using var request = new HttpRequestMessage(
            HttpMethod.Get, new Uri($"features/{Uri.EscapeDataString(id)}", UriKind.Relative));
        var response = await SendAsync(request, ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        await EnsureSuccessAsync(response).ConfigureAwait(false);
        return await response.Content.ReadFromJsonAsync<GeoFeature>(_opts, ct).ConfigureAwait(false);
    }

    public async Task<PagedResult<GeoFeature>> QueryFeaturesAsync(AssetQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("features/query", UriKind.Relative))
        {
            Content = JsonContent.Create(query, options: _opts)
        };
        var response = await SendAsync(request, ct).ConfigureAwait(false);
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
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(relativePath, UriKind.Relative));
        var response = await SendAsync(request, ct).ConfigureAwait(false);
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

        var batchRequest = new TopologyNeighborsBatchRequest
        {
            FeatureIds = featureIds,
            Kind = edgeKind,
            TargetAssetTypeId = targetAssetTypeId
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("features/topology/neighbors-batch", UriKind.Relative))
        {
            Content = JsonContent.Create(batchRequest, options: _opts)
        };
        var response = await SendAsync(request, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response).ConfigureAwait(false);

        var raw = await response.Content.ReadFromJsonAsync<Dictionary<string, List<string>>>(_opts, ct).ConfigureAwait(false);
        if (raw is null)
            return new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

        return raw.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<string>)kv.Value,
            StringComparer.Ordinal);
    }

    public async Task<AssetBatchAppliedEvent> ExecuteAssetDeltaBatchAsync(
        AssetMutationBatchRequestedEvent batch, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(batch);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("mutation-batches", UriKind.Relative))
        {
            Content = JsonContent.Create(batch, options: _opts)
        };
        var response = await SendAsync(request, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response).ConfigureAwait(false);
        return await response.Content.ReadFromJsonAsync<AssetBatchAppliedEvent>(_opts, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Core GeoAssets Server returned an empty mutation-batch apply response.");
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (tokenProvider is not null)
        {
            var token = await tokenProvider.GetAccessTokenAsync(ct).ConfigureAwait(false);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await http.SendAsync(request, ct).ConfigureAwait(false);
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
