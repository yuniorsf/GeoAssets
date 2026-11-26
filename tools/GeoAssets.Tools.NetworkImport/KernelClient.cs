using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using GeoAssets.Core.FeatureImport;
using GeoAssets.Core.Models;
using GeoAssets.Core.Services;

namespace GeoAssets.Tools.NetworkImport;

/// <summary>
/// The kernel's REST surface the tool uses (asset types, bulk feature import, organization-scoped feature reads), with
/// the bearer token and <c>X-Tenant-Domain</c> on every request. Network errors, 5xx and 429 are retried
/// <see cref="MaxAttempts"/> times in all with exponential back-off on the injected clock; any other non-success
/// status, or the last failed attempt, throws <see cref="KernelFailureException"/>.
/// </summary>
internal sealed class KernelClient(HttpClient http, IAccessTokenSource tokens, TimeProvider timeProvider, string? tenantDomain)
{
    public const int MaxAttempts = 3;
    public const string TenantDomainHeader = "X-Tenant-Domain";

    private const string Prefix = "api/geoassets";
    private static readonly TimeSpan FirstBackoff = TimeSpan.FromSeconds(1);

    public async Task<IReadOnlyList<AssetType>> GetAssetTypesAsync(CancellationToken cancellationToken)
    {
        using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, Relative($"{Prefix}/asset-types")), false, cancellationToken).ConfigureAwait(false);
        return await ReadAsync<List<AssetType>>(response, GeoJsonSerializer.Options, cancellationToken).ConfigureAwait(false) ?? [];
    }

    public async Task CreateAssetTypeAsync(AssetType assetType, CancellationToken cancellationToken)
    {
        var body = JsonSerializer.Serialize(assetType, GeoJsonSerializer.Options);
        using var response = await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Post, Relative($"{Prefix}/asset-types"))
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            },
            false,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<FeatureImportChunkResult> PostChunkAsync(
        Guid organizationId, string importId, string scope, byte[] gzipNdjson, CancellationToken cancellationToken)
    {
        var path = $"{ImportPath(organizationId, importId)}/chunks?scope={Uri.EscapeDataString(scope)}";
        using var response = await SendAsync(
            () =>
            {
                var content = new ByteArrayContent(gzipNdjson);
                content.Headers.ContentType = new MediaTypeHeaderValue("application/x-ndjson");
                content.Headers.ContentEncoding.Add("gzip");
                return new HttpRequestMessage(HttpMethod.Post, Relative(path)) { Content = content };
            },
            false,
            cancellationToken).ConfigureAwait(false);
        return await ReadAsync<FeatureImportChunkResult>(response, NetworkImportJson.Wire, cancellationToken).ConfigureAwait(false)
            ?? new FeatureImportChunkResult(0, 0, []);
    }

    public async Task<FeatureImportCompletionResult> CompleteAsync(
        Guid organizationId, string importId, string scope, FeatureImportCompletionMode mode, CancellationToken cancellationToken)
    {
        var body = JsonSerializer.Serialize(new CompletionRequest(scope, mode), NetworkImportJson.Wire);
        using var response = await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Post, Relative($"{ImportPath(organizationId, importId)}/complete"))
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            },
            false,
            cancellationToken).ConfigureAwait(false);
        return await ReadAsync<FeatureImportCompletionResult>(response, NetworkImportJson.Wire, cancellationToken).ConfigureAwait(false)
            ?? new FeatureImportCompletionResult(0);
    }

    /// <summary>The feature as the organization sees it, or <c>null</c> when the kernel answers 404.</summary>
    public async Task<GeoFeature?> GetFeatureAsync(Guid organizationId, string featureId, CancellationToken cancellationToken)
    {
        var path = string.Create(CultureInfo.InvariantCulture, $"{Prefix}/organizations/{organizationId:D}/features/{Uri.EscapeDataString(featureId)}");
        using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, Relative(path)), true, cancellationToken).ConfigureAwait(false);
        return response.StatusCode == HttpStatusCode.NotFound
            ? null
            : await ReadAsync<GeoFeature>(response, GeoJsonSerializer.Options, cancellationToken).ConfigureAwait(false);
    }

    private static string ImportPath(Guid organizationId, string importId) =>
        string.Create(CultureInfo.InvariantCulture, $"{Prefix}/organizations/{organizationId:D}/feature-imports/{Uri.EscapeDataString(importId)}");

    private static Uri Relative(string path) => new(path, UriKind.Relative);

    private async Task<HttpResponseMessage> SendAsync(Func<HttpRequestMessage> createRequest, bool allowNotFound, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            using var request = createRequest();
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokens.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false));
            if (!string.IsNullOrWhiteSpace(tenantDomain))
                request.Headers.TryAddWithoutValidation(TenantDomainHeader, tenantDomain);
            var description = $"{request.Method} /{request.RequestUri}";

            HttpResponseMessage response;
            try
            {
                response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                if (attempt >= MaxAttempts)
                    throw new KernelFailureException($"{description} failed after {MaxAttempts} attempts: {exception.Message}", exception);
                await DelayAsync(attempt, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (response.IsSuccessStatusCode || (allowNotFound && response.StatusCode == HttpStatusCode.NotFound))
                return response;

            using (response)
            {
                var status = (int)response.StatusCode;
                if ((status >= 500 || response.StatusCode == HttpStatusCode.TooManyRequests) && attempt < MaxAttempts)
                {
                    await DelayAsync(attempt, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var detail = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                if (detail.Length > 500)
                    detail = detail[..500];
                throw new KernelFailureException(string.Create(CultureInfo.InvariantCulture, $"{description} answered {status} {response.ReasonPhrase}: {detail}"));
            }
        }
    }

    private Task DelayAsync(int attempt, CancellationToken cancellationToken) =>
        Task.Delay(FirstBackoff * Math.Pow(2, attempt - 1), timeProvider, cancellationToken);

    private static async Task<T?> ReadAsync<T>(HttpResponseMessage response, JsonSerializerOptions options, CancellationToken cancellationToken)
    {
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            try
            {
                return await JsonSerializer.DeserializeAsync<T>(stream, options, cancellationToken).ConfigureAwait(false);
            }
            catch (JsonException exception)
            {
                throw new KernelFailureException($"The kernel answered an unreadable {typeof(T).Name}: {exception.Message}", exception);
            }
        }
    }

    private sealed record CompletionRequest(string Scope, FeatureImportCompletionMode Mode);
}
