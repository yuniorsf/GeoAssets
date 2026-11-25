using System.Net.Http.Json;
using System.Text.Json;
using GeoAssets.Identity.Authorization.Services;

namespace GeoAssets.Web.Services.Identity.Rest;

/// <summary>
/// <see cref="IScopedRoleAdministrationClient"/> backed by <c>GeoAssets.Server</c>'s scoped-role administration routes
/// (XD01-327, <c>OrganizationScopedRolesRestApiExtensions</c>) and its issued-grant writes (XD01-411,
/// <c>OrganizationIssuedGrantsRestApiExtensions</c>). Every non-success answer becomes a
/// <see cref="ScopedRoleAdministrationException"/> carrying its status and, from the problem body, its
/// <see cref="ScopedRoleAssignmentProblems"/> code and detail.
/// </summary>
public sealed class RestScopedRoleAdministrationClient(HttpClient http) : IScopedRoleAdministrationClient
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    public Task<IReadOnlyList<ScopedRoleAssignmentDto>> GetAssignmentsAsync(Guid organizationId, CancellationToken ct = default) =>
        GetListAsync<ScopedRoleAssignmentDto>($"organizations/{organizationId}/scoped-roles", ct);

    public Task<IReadOnlyList<ContractedUserDto>> GetContractedUsersAsync(Guid organizationId, CancellationToken ct = default) =>
        GetListAsync<ContractedUserDto>($"organizations/{organizationId}/contracted-users", ct);

    public Task<IReadOnlyList<OrganizationGrantDto>> GetIssuedGrantsAsync(Guid organizationId, CancellationToken ct = default) =>
        GetListAsync<OrganizationGrantDto>($"organizations/{organizationId}/issued-grants", ct);

    public Task<IReadOnlyList<ScopedRoleAssignmentDto>> GetUserAssignmentsAsync(Guid userId, CancellationToken ct = default) =>
        GetListAsync<ScopedRoleAssignmentDto>($"users/{userId}/scoped-roles", ct);

    public async Task<ScopedRoleAssignmentDto> AssignAsync(Guid organizationId, Guid userId, Guid roleId, CancellationToken ct = default)
    {
        using var response = await http.PostAsJsonAsync(
            new Uri($"organizations/{organizationId}/scoped-roles", UriKind.Relative), new ScopedRoleAssignRequest(userId, roleId), ct);
        await EnsureSuccessAsync(response, ct);

        return await response.Content.ReadFromJsonAsync<ScopedRoleAssignmentDto>(WebJson, ct)
            ?? throw new InvalidOperationException($"POST organizations/{organizationId}/scoped-roles returned an empty response.");
    }

    public async Task RevokeAsync(Guid organizationId, Guid assignmentId, CancellationToken ct = default)
    {
        using var response = await http.DeleteAsync(new Uri($"organizations/{organizationId}/scoped-roles/{assignmentId}", UriKind.Relative), ct);
        await EnsureSuccessAsync(response, ct);
    }

    public async Task<OrganizationGrantDto> IssueGrantAsync(Guid organizationId, IssuedGrantCreateRequest request, CancellationToken ct = default)
    {
        using var response = await http.PostAsJsonAsync(
            new Uri($"organizations/{organizationId}/issued-grants", UriKind.Relative), request, WebJson, ct);
        await EnsureSuccessAsync(response, ct);

        return await response.Content.ReadFromJsonAsync<OrganizationGrantDto>(WebJson, ct)
            ?? throw new InvalidOperationException($"POST organizations/{organizationId}/issued-grants returned an empty response.");
    }

    public async Task<OrganizationGrantDto> UpdateGrantAsync(Guid organizationId, Guid grantId, IssuedGrantRules rules, CancellationToken ct = default)
    {
        using var response = await http.PutAsJsonAsync(
            new Uri($"organizations/{organizationId}/issued-grants/{grantId}", UriKind.Relative), new IssuedGrantUpdateRequest(rules), WebJson, ct);
        await EnsureSuccessAsync(response, ct);

        return await response.Content.ReadFromJsonAsync<OrganizationGrantDto>(WebJson, ct)
            ?? throw new InvalidOperationException($"PUT organizations/{organizationId}/issued-grants/{grantId} returned an empty response.");
    }

    public async Task RevokeGrantAsync(Guid organizationId, Guid grantId, CancellationToken ct = default)
    {
        using var response = await http.DeleteAsync(new Uri($"organizations/{organizationId}/issued-grants/{grantId}", UriKind.Relative), ct);
        await EnsureSuccessAsync(response, ct);
    }

    private async Task<IReadOnlyList<T>> GetListAsync<T>(string path, CancellationToken ct)
    {
        using var response = await http.GetAsync(new Uri(path, UriKind.Relative), ct);
        await EnsureSuccessAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<List<T>>(WebJson, ct) ?? [];
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;

        string? code = null;
        string? detail = null;
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                using var document = JsonDocument.Parse(body);
                if (document.RootElement.ValueKind == JsonValueKind.Object)
                {
                    code = ReadString(document.RootElement, "code");
                    detail = ReadString(document.RootElement, "detail");
                }
            }
            catch (JsonException)
            {
                // Not a problem body (e.g. a proxy's HTML error page): only the status is known.
            }
        }

        throw new ScopedRoleAdministrationException(
            (int)response.StatusCode, code, detail ?? $"The server answered {(int)response.StatusCode} {response.ReasonPhrase}.");
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
