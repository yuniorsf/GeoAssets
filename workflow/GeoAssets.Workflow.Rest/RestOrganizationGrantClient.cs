using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GeoAssets.Identity.Authentication;
using GeoAssets.Identity.Authorization.Services;

namespace GeoAssets.Workflow.Rest;

/// <summary>
/// HTTP implementation of <see cref="IOrganizationGrantClient"/> relative to <paramref name="http"/>'s base address — the
/// kernel's root, e.g. <c>http://geoassets-server:8080/</c>. Every request carries a fresh service-principal bearer token
/// from <paramref name="tokenProvider"/> when one is registered; without one the kernel answers 401 and the call fails.
/// </summary>
public sealed class RestOrganizationGrantClient(HttpClient http, IServiceAuthTokenProvider? tokenProvider = null) : IOrganizationGrantClient
{
    private const string Route = "api/identity/organization-grants";

    private static readonly JsonSerializerOptions _opts = new(JsonSerializerDefaults.Web);

    public Task<OrganizationGrantUpsertResult> UpsertAsync(OrganizationGrantUpsertRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        return SendAsync<OrganizationGrantUpsertResult>(
            HttpMethod.Put, Route, JsonContent.Create(request, options: _opts), ct, conflictKey: request.Key);
    }

    public Task<OrganizationGrantRevokeResult> RevokeAsync(OrganizationGrantKey key, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(key);

        return SendAsync<OrganizationGrantRevokeResult>(HttpMethod.Post, $"{Route}/revoke", JsonContent.Create(key, options: _opts), ct);
    }

    public Task<OrganizationGrantPage> ListActiveAsync(
        Guid granteeOrganizationId, Guid resourceOrganizationId, int page, int pageSize, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);

        var query = string.Create(
            CultureInfo.InvariantCulture,
            $"?granteeOrganizationId={granteeOrganizationId}&resourceOrganizationId={resourceOrganizationId}&page={page}&pageSize={pageSize}");
        return SendAsync<OrganizationGrantPage>(HttpMethod.Get, Route + query, content: null, ct);
    }

    private async Task<T> SendAsync<T>(
        HttpMethod method, string path, HttpContent? content, CancellationToken ct, OrganizationGrantKey? conflictKey = null)
    {
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative)) { Content = content };

        if (tokenProvider is not null)
        {
            var token = await tokenProvider.GetAccessTokenAsync(ct).ConfigureAwait(false);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
        if (conflictKey is not null && response.StatusCode == HttpStatusCode.Conflict
            && await ReadConflictDetailAsync(response, ct).ConfigureAwait(false) is { IsConflict: true } conflict)
        {
            throw new OrganizationGrantConflictException(conflictKey, conflict.Detail);
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(_opts, ct).ConfigureAwait(false)
            ?? throw new HttpRequestException($"The kernel's {method} /{Route} answered an empty body.");
    }

    /// <summary>
    /// Whether a 409's body is the kernel's <see cref="OrganizationGrantConflictException.ProblemCode"/> problem (XD01-401), and
    /// its <c>detail</c>. Anything else — no body, not JSON, not an object, another code — is not, and keeps the plain 409.
    /// </summary>
    private static async Task<(bool IsConflict, string? Detail)> ReadConflictDetailAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using (stream.ConfigureAwait(false))
            {
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object
                    || !root.TryGetProperty("code", out var code) || code.ValueKind != JsonValueKind.String
                    || !string.Equals(code.GetString(), OrganizationGrantConflictException.ProblemCode, StringComparison.Ordinal))
                {
                    return (false, null);
                }

                var detail = root.TryGetProperty("detail", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;
                return (true, detail);
            }
        }
        catch (JsonException)
        {
            return (false, null);
        }
    }
}
