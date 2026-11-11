using System.Net.Http.Json;
using GeoAssets.Workflow.Isp.NeutralOperator.Leasing.Settlement;
using Microsoft.Extensions.Configuration;

namespace GeoAssets.FieldService.Server;

/// <summary>
/// REST-backed <see cref="IErpFinancialPostingClient"/> (XD01-268). The base URL comes from
/// <c>Erp:LeaseSettlement:BaseUrl</c> — same posture as <c>HttpTotvsProtheusSyncClient</c>
/// (XD01-253): no real ERP endpoint is configured by default, a missing one throws rather than
/// silently no-op'ing, and the request body is GeoAssets's own DTO shape (<see
/// cref="LeaseInvoiceErpPostingRequest"/>), not either vendor's native wire format. Retry/backoff
/// for ERP unreachability is <see cref="LeaseInvoiceSettlementOrchestrator"/>'s job via its
/// idempotent resume, not this client's — it makes exactly one attempt per call.
/// </summary>
internal sealed class HttpErpFinancialPostingClient(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    : IErpFinancialPostingClient
{
    public const string HttpClientName = "LeaseSettlementErpPosting";

    public async Task PostAsync(LeaseInvoiceErpPostingRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var baseUrl = configuration["Erp:LeaseSettlement:BaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException("No ERP lease-settlement base URL configured (Erp:LeaseSettlement:BaseUrl).");
        }

        var client = httpClientFactory.CreateClient(HttpClientName);
        client.BaseAddress = new Uri(baseUrl, UriKind.Absolute);

        using var response = await client.PostAsJsonAsync("lease-invoices", request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }
}
