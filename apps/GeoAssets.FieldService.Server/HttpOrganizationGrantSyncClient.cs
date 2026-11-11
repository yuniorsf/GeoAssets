using System.Net.Http.Json;
using System.Text.Json;
using GeoAssets.Identity.Authorization;
using GeoAssets.Identity.Authorization.Models;
using Microsoft.Extensions.Configuration;

namespace GeoAssets.FieldService.Server;

/// <summary>
/// HTTP implementation of <see cref="IOrganizationGrantSyncClient"/> (XD01-290), calling Core
/// GeoAssets Server's <c>GET /api/identity/organization-grants/sync</c>. Base URL read from
/// <c>OrganizationGrantSync:CoreApiBaseUrl</c> — same "read the absolute URL from configuration
/// per call, no fixed HttpClient.BaseAddress" idiom as <see cref="HttpBssOssOutboundWebhookDispatcher"/>,
/// since this host already resolves <c>IGeoCoreClient</c> per-catalog/organization via
/// <c>IGeoCoreClientResolver</c> for GIS calls, but grants aren't catalog-sharded — there is
/// exactly one identity host to ask, so a second resolver would be over-engineering.
/// </summary>
internal sealed class HttpOrganizationGrantSyncClient(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    : IOrganizationGrantSyncClient
{
    public const string HttpClientName = "OrganizationGrantSync";

    private static readonly JsonSerializerOptions _opts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public async Task<IReadOnlyList<OrganizationGrantSyncEntry>> GetSyncBatchAsync(
        long afterSequence, int take, CancellationToken ct = default)
    {
        var baseUrl = configuration["OrganizationGrantSync:CoreApiBaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException(
                "No Core GeoAssets Server base URL configured for OrganizationGrant sync " +
                "(OrganizationGrantSync:CoreApiBaseUrl).");
        }

        var url = new Uri($"{baseUrl.TrimEnd('/')}/organization-grants/sync?after={afterSequence}&take={take}");
        var client = httpClientFactory.CreateClient(HttpClientName);
        using var response = await client.GetAsync(url, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var batch = await response.Content
            .ReadFromJsonAsync<List<OrganizationGrantSyncEntry>>(_opts, ct)
            .ConfigureAwait(false);
        return batch ?? [];
    }
}
