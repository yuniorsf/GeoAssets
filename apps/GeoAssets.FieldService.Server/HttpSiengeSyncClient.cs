using System.Globalization;
using System.Net.Http.Json;
using GeoAssets.Workflow.Erp.Sienge;
using Microsoft.Extensions.Configuration;

namespace GeoAssets.FieldService.Server;

/// <summary>
/// REST-backed <see cref="ISiengeSyncClient"/> (XD01-254). The base URL comes from
/// <c>Erp:Sienge:BaseUrl</c> — no real Sienge credentials or endpoint are configured by default
/// in this environment, so a missing configuration throws rather than silently no-op'ing.
/// Individual HTTP failures propagate as exceptions; <see cref="SiengeRetryExecutor"/> (called by
/// <see cref="SiengeNightlySyncOrchestrator"/>) is what applies retry/backoff — this client itself
/// makes exactly one attempt per call.
/// </summary>
internal sealed class HttpSiengeSyncClient(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    : ISiengeSyncClient
{
    public const string HttpClientName = "SiengeSync";

    public async Task PushCostCenterPostingsAsync(IReadOnlyList<SiengeCostCenterPostingLine> postings, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(postings);

        var client = BuildClient();
        using var response = await client.PostAsJsonAsync("cost-centers/postings", postings, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<SiengePurchaseOrderRecord>> PullPurchaseOrdersSinceAsync(DateTime sinceUtc, CancellationToken ct = default)
    {
        var client = BuildClient();
        var path = $"purchase-orders?sinceUtc={Uri.EscapeDataString(sinceUtc.ToString("O", CultureInfo.InvariantCulture))}";
        var records = await client.GetFromJsonAsync<List<SiengePurchaseOrderRecord>>(path, ct).ConfigureAwait(false);
        return records ?? [];
    }

    private HttpClient BuildClient()
    {
        var baseUrl = configuration["Erp:Sienge:BaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException("No Sienge base URL configured (Erp:Sienge:BaseUrl).");
        }

        var client = httpClientFactory.CreateClient(HttpClientName);
        client.BaseAddress = new Uri(baseUrl, UriKind.Absolute);
        return client;
    }
}
