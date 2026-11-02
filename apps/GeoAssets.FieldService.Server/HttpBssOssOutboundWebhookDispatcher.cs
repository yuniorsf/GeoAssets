using System.Net.Http.Json;
using GeoAssets.Workflow.Isp.Bss;
using Microsoft.Extensions.Configuration;

namespace GeoAssets.FieldService.Server;

/// <summary>
/// POSTs an activation payload to the target BSS/OSS provider's own callback URL, read from
/// <c>Bss:Webhooks:{providerId}:ActivationCallbackUrl</c>. No real vendor credentials or endpoint
/// are configured by default in this environment — a provider with no configured URL throws
/// rather than silently dropping the notification, so the gap is visible instead of masked.
/// </summary>
internal sealed class HttpBssOssOutboundWebhookDispatcher(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    : IBssOssOutboundWebhookDispatcher
{
    public const string HttpClientName = "BssOssOutboundWebhook";

    public async Task DispatchAsync(string providerId, string payloadJson, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(providerId);
        ArgumentException.ThrowIfNullOrEmpty(payloadJson);

        var url = configuration[$"Bss:Webhooks:{providerId}:ActivationCallbackUrl"];
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new InvalidOperationException(
                $"No activation callback URL configured for BSS/OSS provider '{providerId}' (Bss:Webhooks:{providerId}:ActivationCallbackUrl).");
        }

        var client = httpClientFactory.CreateClient(HttpClientName);
        using var content = JsonContent.Create(System.Text.Json.JsonDocument.Parse(payloadJson).RootElement);
        using var response = await client.PostAsync(new Uri(url), content, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }
}
