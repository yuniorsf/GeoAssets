using System.Net.Http.Json;
using GeoAssets.Workflow.Erp.Sap;
using Microsoft.Extensions.Configuration;

namespace GeoAssets.FieldService.Server;

/// <summary>
/// POSTs an order confirmation to SAP's inbound OData facade over
/// <c>BAPI_ALM_ORDER_CONFIRMATION_CREATE</c>, read from <c>Sap:Pm:ConfirmationCallbackUrl</c>. No
/// real SAP endpoint or credentials are configured by default in this environment — an unset URL
/// throws rather than silently dropping the confirmation. Same shape as
/// <see cref="HttpBssOssOutboundWebhookDispatcher"/>.
/// </summary>
internal sealed class HttpSapPmConfirmationDispatcher(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    : ISapPmConfirmationDispatcher
{
    public const string HttpClientName = "SapPmConfirmation";

    public async Task DispatchAsync(SapPmOrderConfirmation confirmation, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(confirmation);

        var url = configuration["Sap:Pm:ConfirmationCallbackUrl"];
        if (string.IsNullOrWhiteSpace(url))
            throw new InvalidOperationException("No SAP PM confirmation callback URL configured (Sap:Pm:ConfirmationCallbackUrl).");

        var client = httpClientFactory.CreateClient(HttpClientName);
        using var response = await client.PostAsJsonAsync(new Uri(url), confirmation, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }
}
