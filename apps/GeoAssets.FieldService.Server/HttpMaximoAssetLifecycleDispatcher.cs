using System.Net.Http.Json;
using GeoAssets.Core.Erp.Maximo;
using Microsoft.Extensions.Configuration;

namespace GeoAssets.FieldService.Server;

/// <summary>
/// POSTs an asset lifecycle update to IBM Maximo's MIF inbound endpoint, read from
/// <c>Maximo:Mif:AssetLifecycleUrl</c>. No real Maximo endpoint or credentials are configured by
/// default in this environment — an unset URL throws rather than silently dropping the update.
/// Same shape as <see cref="HttpSapPmConfirmationDispatcher"/>.
/// </summary>
internal sealed class HttpMaximoAssetLifecycleDispatcher(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    : IMaximoAssetLifecycleDispatcher
{
    public const string HttpClientName = "MaximoAssetLifecycle";

    public async Task DispatchAsync(MaximoAssetLifecycleUpdate update, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(update);

        var url = configuration["Maximo:Mif:AssetLifecycleUrl"];
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new InvalidOperationException("No Maximo MIF asset lifecycle URL configured (Maximo:Mif:AssetLifecycleUrl).");
        }

        var client = httpClientFactory.CreateClient(HttpClientName);
        using var response = await client.PostAsJsonAsync(new Uri(url), update, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }
}
