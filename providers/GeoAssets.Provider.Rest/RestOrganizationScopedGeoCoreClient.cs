using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Services;
using GeoAssets.Identity.Authentication;

namespace GeoAssets.Provider.Rest;

/// <summary>
/// HTTP implementation of <see cref="IOrganizationScopedGeoCoreClient"/> over the Core GeoAssets
/// Server's service-caller-only routes (XD01-386), relative to the same base address as
/// <see cref="RestGeoCoreClient"/> (e.g. <c>http://core/api/geoassets/</c>):
/// <c>GET organizations/{organizationId}/features/{id}</c> and
/// <c>GET organizations/{organizationId}/features/{id}/descendants?edgeKind=&amp;skip=&amp;take=</c>.
/// Authenticates exactly like <see cref="RestGeoCoreClient"/> — a fresh service-principal
/// <c>Authorization: Bearer</c> token per request from <paramref name="tokenProvider"/> when one
/// is registered.
///
/// <see cref="GetDescendantsAsync"/> requests pages of <see cref="PageSize"/> until the server's
/// <c>TotalCount</c> is reached, but never collects more than <see cref="MaxFeatures"/> features:
/// a real feeder segment is orders of magnitude smaller, so reaching the cap means a misconfigured
/// topology, and the walk stops there rather than pulling the whole network across the wire.
/// </summary>
public sealed class RestOrganizationScopedGeoCoreClient(HttpClient http, IServiceAuthTokenProvider? tokenProvider = null)
    : IOrganizationScopedGeoCoreClient
{
    /// <summary>Features requested per descendants page.</summary>
    public const int PageSize = 500;

    /// <summary>Upper bound on the features one <see cref="GetDescendantsAsync"/> call collects.</summary>
    public const int MaxFeatures = 10_000;

    private static readonly JsonSerializerOptions _opts = GeoJsonSerializer.Options;

    public async Task<GeoFeature?> GetFeatureAsync(Guid organizationId, string id, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(FeaturePath(organizationId, id), UriKind.Relative));
        using var response = await SendAsync(request, ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
        return await response.Content.ReadFromJsonAsync<GeoFeature>(_opts, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<GeoFeature>> GetDescendantsAsync(
        Guid organizationId, string id, string edgeKind, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(edgeKind);

        var features = new List<GeoFeature>();
        while (features.Count < MaxFeatures)
        {
            var path = string.Create(
                CultureInfo.InvariantCulture,
                $"{FeaturePath(organizationId, id)}/descendants?edgeKind={Uri.EscapeDataString(edgeKind)}&skip={features.Count}&take={PageSize}");
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
            using var response = await SendAsync(request, ct).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound) return features;
            await EnsureSuccessAsync(response, ct).ConfigureAwait(false);

            var page = await response.Content.ReadFromJsonAsync<PagedResult<GeoFeature>>(_opts, ct).ConfigureAwait(false);
            if (page is null || page.Items.Count == 0) break;

            features.AddRange(page.Items.Take(MaxFeatures - features.Count));
            if (features.Count >= page.TotalCount) break;
        }

        return features;
    }

    private static string FeaturePath(Guid organizationId, string id) =>
        $"organizations/{organizationId.ToString("D", CultureInfo.InvariantCulture)}/features/{Uri.EscapeDataString(id)}";

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (tokenProvider is not null)
        {
            var token = await tokenProvider.GetAccessTokenAsync(ct).ConfigureAwait(false);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await http.SendAsync(request, ct).ConfigureAwait(false);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new UnauthorizedAccessException(
                string.IsNullOrWhiteSpace(body) ? "Not authorized." : body);
        }

        response.EnsureSuccessStatusCode();
    }
}
