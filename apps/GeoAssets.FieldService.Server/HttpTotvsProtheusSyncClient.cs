using System.Globalization;
using System.Net.Http.Json;
using GeoAssets.Workflow.Erp.Totvs;
using Microsoft.Extensions.Configuration;

namespace GeoAssets.FieldService.Server;

/// <summary>
/// REST-backed <see cref="ITotvsProtheusSyncClient"/> (XD01-253). The base URL comes from
/// <c>Erp:Totvs:BaseUrl</c> — no real Totvs Protheus credentials or endpoint are configured by
/// default in this environment, so a missing configuration throws rather than silently
/// no-op'ing. Individual HTTP failures propagate as exceptions; <c>TotvsRetryExecutor</c> (called
/// by <c>TotvsNightlySyncOrchestrator</c>) is what applies retry/backoff — this client itself
/// makes exactly one attempt per call.
/// </summary>
internal sealed class HttpTotvsProtheusSyncClient(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    : ITotvsProtheusSyncClient
{
    public const string HttpClientName = "TotvsProtheusSync";

    public async Task<IReadOnlyList<TechnicianComplianceRecord>> PullTechnicianComplianceAsync(CancellationToken ct = default)
    {
        var client = BuildClient();
        var records = await client.GetFromJsonAsync<List<TechnicianComplianceRecord>>("technicians/compliance", ct).ConfigureAwait(false);
        return records ?? [];
    }

    public async Task PushPayrollExportAsync(IReadOnlyList<ProtheusPayrollExportLine> lines, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var client = BuildClient();
        using var response = await client.PostAsJsonAsync("payroll/export", lines, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<WarehouseStockIssuanceRecord>> PullWarehouseStockIssuancesAsync(DateTime sinceUtc, CancellationToken ct = default)
    {
        var client = BuildClient();
        var path = $"warehouse/stock-issuances?sinceUtc={Uri.EscapeDataString(sinceUtc.ToString("O", CultureInfo.InvariantCulture))}";
        var records = await client.GetFromJsonAsync<List<WarehouseStockIssuanceRecord>>(path, ct).ConfigureAwait(false);
        return records ?? [];
    }

    private HttpClient BuildClient()
    {
        var baseUrl = configuration["Erp:Totvs:BaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException("No Totvs Protheus base URL configured (Erp:Totvs:BaseUrl).");
        }

        var client = httpClientFactory.CreateClient(HttpClientName);
        client.BaseAddress = new Uri(baseUrl, UriKind.Absolute);
        return client;
    }
}
