using System.Net.Http.Json;
using GeoAssets.Workflow.Contractors.Governance;
using Microsoft.Extensions.Configuration;

namespace GeoAssets.FieldService.Server;

/// <summary>
/// Pushes measurement sheets and queries contract balance against the contractor governance
/// portal (SAP WeBUY / CEMIG Portal de Fornecedores, XD01-238), read from
/// <c>ContractorGovernancePortal:MeasurementSheetPushUrl</c> and
/// <c>ContractorGovernancePortal:ContractBalanceQueryBaseUrl</c>. No real portal endpoint or
/// credentials are configured by default in this environment — an unset URL throws rather than
/// silently dropping the call. Same shape as <see cref="HttpBssOssOutboundWebhookDispatcher"/>.
/// </summary>
internal sealed class HttpContractorGovernancePortalClient(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    : IContractorGovernancePortalClient
{
    public const string HttpClientName = "ContractorGovernancePortal";

    public async Task PushMeasurementSheetAsync(MeasurementSheetPortalPayload payload, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var url = configuration["ContractorGovernancePortal:MeasurementSheetPushUrl"];
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new InvalidOperationException(
                "No measurement-sheet push URL configured (ContractorGovernancePortal:MeasurementSheetPushUrl).");
        }

        var client = httpClientFactory.CreateClient(HttpClientName);
        using var response = await client.PostAsJsonAsync(new Uri(url), payload, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    public async Task<ContractBalanceQueryResult> QueryContractBalanceAsync(string contractId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contractId);

        var baseUrl = configuration["ContractorGovernancePortal:ContractBalanceQueryBaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException(
                "No contract-balance query base URL configured (ContractorGovernancePortal:ContractBalanceQueryBaseUrl).");
        }

        var client = httpClientFactory.CreateClient(HttpClientName);
        var url = new Uri($"{baseUrl.TrimEnd('/')}/{Uri.EscapeDataString(contractId)}/balance");
        var result = await client.GetFromJsonAsync<ContractBalanceQueryResult>(url, ct).ConfigureAwait(false);
        return result ?? throw new InvalidOperationException(
            $"Contract balance portal returned an empty response for contract '{contractId}'.");
    }
}
