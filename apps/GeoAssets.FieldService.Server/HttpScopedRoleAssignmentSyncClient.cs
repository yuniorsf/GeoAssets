using System.Net.Http.Json;
using System.Text.Json;
using GeoAssets.Identity.Authorization;
using GeoAssets.Identity.Authorization.Models;
using Microsoft.Extensions.Configuration;

namespace GeoAssets.FieldService.Server;

/// <summary>
/// HTTP implementation of <see cref="IScopedRoleAssignmentSyncClient"/> (XD01-328), calling Core GeoAssets Server's
/// <c>GET /api/identity/scoped-role-assignments/sync</c> — the twin of <see cref="HttpOrganizationGrantSyncClient"/>, on the
/// same base URL (<c>OrganizationGrantSync:CoreApiBaseUrl</c>) and HTTP client name.
/// </summary>
internal sealed class HttpScopedRoleAssignmentSyncClient(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    : IScopedRoleAssignmentSyncClient
{
    private static readonly JsonSerializerOptions _opts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public async Task<IReadOnlyList<ScopedRoleAssignmentSyncEntry>> GetSyncBatchAsync(
        long afterSequence, int take, CancellationToken ct = default)
    {
        var baseUrl = configuration["OrganizationGrantSync:CoreApiBaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException(
                "No Core GeoAssets Server base URL configured for scoped role assignment sync " +
                "(OrganizationGrantSync:CoreApiBaseUrl).");
        }

        var url = new Uri($"{baseUrl.TrimEnd('/')}/scoped-role-assignments/sync?after={afterSequence}&take={take}");
        var client = httpClientFactory.CreateClient(HttpOrganizationGrantSyncClient.HttpClientName);
        using var response = await client.GetAsync(url, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var batch = await response.Content
            .ReadFromJsonAsync<List<ScopedRoleAssignmentSyncEntry>>(_opts, ct)
            .ConfigureAwait(false);
        return batch ?? [];
    }
}
