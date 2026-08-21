using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Services;

namespace GeoAssets.Server;

/// <summary>
/// <see cref="IRoleAssignmentProvider"/> implementation backed by Microsoft Graph (XD01-59 Phase
/// 2, XD01-62). Server-only — this class, its <see cref="HttpClient"/>, and the credential
/// behind <see cref="IGraphAccessTokenProvider"/> must never be referenced from
/// <c>GeoAssets.Web</c>/WASM.
///
/// Pushes role registration/assignment to <em>every</em> app registration listed in
/// <see cref="RoleSyncOptions.TargetApplicationClientIds"/> — typically both GeoAssets Web and
/// GeoAssets Server, since which one issues the token GeoAssets reads the <c>roles</c> claim
/// from depends on <c>Identity:Backend</c> (confirmed directly during this project's Phase 1
/// manual testing: <c>InMemory</c> reads the WASM client's own sign-in token, <c>Rest</c> reads
/// the Server-audienced token). Callers of <see cref="IRoleAssignmentProvider"/> never need to
/// know this — it's entirely internal to this implementation.
///
/// Addresses each application/service principal by its Client (Application) ID via Graph's
/// alternate-key syntax (<c>applications(appId='...')</c> /
/// <c>servicePrincipals(appId='...')</c>) rather than requiring the directory Object ID in
/// configuration — one less opaque GUID an operator has to go capture during XD01-60's setup.
/// </summary>
public sealed class EntraGraphRoleAssignmentProvider : IRoleAssignmentProvider
{
    private readonly HttpClient _graph;
    private readonly IGraphAccessTokenProvider _tokenProvider;
    private readonly RoleSyncOptions _options;

    // appRoles is a whole-collection replace, not a diff (Graph's own semantics) — this
    // serializes GeoAssets-originated writes per app registration so two concurrent admins (or
    // two concurrent calls from this process) can't silently drop each other's roles. A
    // concurrent *direct* Entra-portal edit during a sync is a separate, accepted risk (not
    // solvable without distributed locking Graph doesn't offer) — see XD01-59/62's design notes.
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _appLocks = new();

    // Service principal ids don't change for the lifetime of this instance — fetch once per
    // app registration, not on every assignment call.
    private readonly ConcurrentDictionary<string, string> _servicePrincipalIds = new();

    public EntraGraphRoleAssignmentProvider(
        HttpClient graph, IGraphAccessTokenProvider tokenProvider, RoleSyncOptions options)
    {
        _graph         = graph;
        _tokenProvider = tokenProvider;
        _options       = options;
    }

    public async Task RegisterRoleAsync(AppRole role, CancellationToken ct = default)
    {
        foreach (var clientId in _options.TargetApplicationClientIds)
        {
            var appLock = GetLock(clientId);
            await appLock.WaitAsync(ct);
            try
            {
                var roles = await GetAppRolesAsync(clientId, ct);
                var updated = new GraphAppRole(role.Id, role.Name, role.Description, role.Name, IsEnabled: true, ["User"]);

                var index = roles.FindIndex(r => r.Id == role.Id);
                if (index >= 0) roles[index] = updated;
                else roles.Add(updated);

                await PatchAppRolesAsync(clientId, roles, ct);
            }
            finally
            {
                appLock.Release();
            }
        }
    }

    public async Task UnregisterRoleAsync(Guid roleId, CancellationToken ct = default)
    {
        foreach (var clientId in _options.TargetApplicationClientIds)
        {
            var appLock = GetLock(clientId);
            await appLock.WaitAsync(ct);
            try
            {
                var roles = await GetAppRolesAsync(clientId, ct);
                var index = roles.FindIndex(r => r.Id == roleId);
                if (index < 0) continue; // never registered against this app — no-op

                // Two-phase per Graph's own requirement: a role must be disabled in one PATCH
                // before a follow-up PATCH can remove it from the appRoles array.
                roles[index] = roles[index] with { IsEnabled = false };
                await PatchAppRolesAsync(clientId, roles, ct);

                roles.RemoveAt(index);
                await PatchAppRolesAsync(clientId, roles, ct);
            }
            finally
            {
                appLock.Release();
            }
        }
    }

    public async Task AssignRoleAsync(string externalUserObjectId, string roleName, CancellationToken ct = default)
    {
        foreach (var clientId in _options.TargetApplicationClientIds)
        {
            var appRole = await FindAppRoleByNameAsync(clientId, roleName, ct)
                ?? throw new InvalidOperationException(
                    $"Role '{roleName}' is not registered against application '{clientId}'. Call RegisterRoleAsync first.");

            var servicePrincipalId = await GetServicePrincipalIdAsync(clientId, ct);
            var assignments = await GetAssignmentsAsync(servicePrincipalId, ct);

            if (assignments.Any(a => a.PrincipalId == externalUserObjectId && a.AppRoleId == appRole.Id))
                continue; // already assigned — idempotent no-op

            await SendAsync(HttpMethod.Post, $"servicePrincipals/{servicePrincipalId}/appRoleAssignedTo", new
            {
                principalId = externalUserObjectId,
                resourceId  = servicePrincipalId,
                appRoleId   = appRole.Id,
            }, ct);
        }
    }

    public async Task RevokeRoleAsync(string externalUserObjectId, string roleName, CancellationToken ct = default)
    {
        foreach (var clientId in _options.TargetApplicationClientIds)
        {
            var appRole = await FindAppRoleByNameAsync(clientId, roleName, ct);
            if (appRole is null) continue; // never registered against this app — nothing to revoke

            var servicePrincipalId = await GetServicePrincipalIdAsync(clientId, ct);
            var assignments = await GetAssignmentsAsync(servicePrincipalId, ct);
            var assignment = assignments.FirstOrDefault(a => a.PrincipalId == externalUserObjectId && a.AppRoleId == appRole.Id);
            if (assignment is null) continue; // not currently assigned — no-op

            await SendAsync(HttpMethod.Delete, $"servicePrincipals/{servicePrincipalId}/appRoleAssignedTo/{assignment.Id}", body: null, ct);
        }
    }

    public async Task<IReadOnlyList<string>> GetAssignedRoleNamesAsync(string externalUserObjectId, CancellationToken ct = default)
    {
        var names = new HashSet<string>();

        foreach (var clientId in _options.TargetApplicationClientIds)
        {
            var roles = await GetAppRolesAsync(clientId, ct);
            var servicePrincipalId = await GetServicePrincipalIdAsync(clientId, ct);
            var assignments = await GetAssignmentsAsync(servicePrincipalId, ct);

            foreach (var assignment in assignments.Where(a => a.PrincipalId == externalUserObjectId))
            {
                var role = roles.Find(r => r.Id == assignment.AppRoleId);
                if (role is not null) names.Add(role.Value);
            }
        }

        return names.ToList();
    }

    // ── Graph mechanics ───────────────────────────────────────────────────────

    private async Task<GraphAppRole?> FindAppRoleByNameAsync(string clientId, string roleName, CancellationToken ct)
    {
        var roles = await GetAppRolesAsync(clientId, ct);
        return roles.Find(r => r.Value == roleName);
    }

    private async Task<List<GraphAppRole>> GetAppRolesAsync(string clientId, CancellationToken ct)
    {
        var response = await SendAsync(HttpMethod.Get, $"applications(appId='{Uri.EscapeDataString(clientId)}')?$select=appRoles", body: null, ct);
        var dto = await response.Content.ReadFromJsonAsync<GraphAppRolesResponse>(ct)
            ?? throw new InvalidOperationException($"GET applications(appId='{clientId}') returned an empty response.");
        return dto.AppRoles;
    }

    private async Task PatchAppRolesAsync(string clientId, List<GraphAppRole> roles, CancellationToken ct)
    {
        await SendAsync(HttpMethod.Patch, $"applications(appId='{Uri.EscapeDataString(clientId)}')", new { appRoles = roles }, ct);
    }

    private async Task<string> GetServicePrincipalIdAsync(string clientId, CancellationToken ct)
    {
        if (_servicePrincipalIds.TryGetValue(clientId, out var cached))
            return cached;

        var response = await SendAsync(HttpMethod.Get, $"servicePrincipals(appId='{Uri.EscapeDataString(clientId)}')?$select=id", body: null, ct);
        var dto = await response.Content.ReadFromJsonAsync<GraphIdResponse>(ct)
            ?? throw new InvalidOperationException($"GET servicePrincipals(appId='{clientId}') returned an empty response.");

        _servicePrincipalIds[clientId] = dto.Id;
        return dto.Id;
    }

    private async Task<List<GraphAppRoleAssignment>> GetAssignmentsAsync(string servicePrincipalId, CancellationToken ct)
    {
        var response = await SendAsync(HttpMethod.Get, $"servicePrincipals/{servicePrincipalId}/appRoleAssignedTo", body: null, ct);
        var dto = await response.Content.ReadFromJsonAsync<GraphAssignmentsResponse>(ct)
            ?? throw new InvalidOperationException("GET appRoleAssignedTo returned an empty response.");
        return dto.Value;
    }

    private SemaphoreSlim GetLock(string clientId) => _appLocks.GetOrAdd(clientId, _ => new SemaphoreSlim(1, 1));

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string relativeUrl, object? body, CancellationToken ct)
    {
        var token = await _tokenProvider.GetAccessTokenAsync(ct);
        var request = new HttpRequestMessage(method, relativeUrl)
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) },
        };
        if (body is not null)
            request.Content = JsonContent.Create(body);

        var response = await _graph.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return response;
    }

    // ── Graph wire shapes (minimal — only the fields this provider reads/writes) ───────────

    private sealed record GraphAppRolesResponse([property: JsonPropertyName("appRoles")] List<GraphAppRole> AppRoles);

    private sealed record GraphAppRole(
        [property: JsonPropertyName("id")] Guid Id,
        [property: JsonPropertyName("displayName")] string DisplayName,
        [property: JsonPropertyName("description")] string Description,
        [property: JsonPropertyName("value")] string Value,
        [property: JsonPropertyName("isEnabled")] bool IsEnabled,
        [property: JsonPropertyName("allowedMemberTypes")] List<string> AllowedMemberTypes);

    private sealed record GraphIdResponse([property: JsonPropertyName("id")] string Id);

    private sealed record GraphAssignmentsResponse([property: JsonPropertyName("value")] List<GraphAppRoleAssignment> Value);

    private sealed record GraphAppRoleAssignment(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("principalId")] string PrincipalId,
        [property: JsonPropertyName("resourceId")] string ResourceId,
        [property: JsonPropertyName("appRoleId")] Guid AppRoleId);
}
