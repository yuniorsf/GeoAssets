using System.Net.Http.Json;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Services;

namespace GeoAssets.Web.Services.Identity.Rest;

/// <summary>
/// <see cref="IRoleAssignmentProvider"/> backed by <c>GeoAssets.Server</c>'s
/// <c>/api/identity/rolesync/*</c> endpoints (XD01-63). The Graph credential and the real
/// implementation (<c>EntraGraphRoleAssignmentProvider</c>, XD01-62) live only server-side —
/// this class is a thin HTTP proxy and never talks to Microsoft Graph directly from the browser.
///
/// <see cref="UnregisterRoleAsync"/> has no matching server endpoint — the admin UI (XD01-63)
/// only exposes a "Register" action, not "Unregister" — and throws
/// <see cref="NotSupportedException"/>, the same idiom the other Rest* repositories already use
/// for operations their server surface doesn't expose (see <see cref="RestRoleRepository.GetByNameAsync"/>).
/// </summary>
public sealed class RestRoleAssignmentProvider(HttpClient http) : IRoleAssignmentProvider
{
    public async Task RegisterRoleAsync(AppRole role, CancellationToken ct = default)
    {
        var response = await http.PostAsync($"rolesync/roles/{role.Id}", content: null, ct);
        response.EnsureSuccessStatusCode();
    }

    public Task UnregisterRoleAsync(Guid roleId, CancellationToken ct = default) => throw new NotSupportedException();

    public async Task AssignRoleAsync(string externalUserObjectId, string roleName, CancellationToken ct = default)
    {
        var response = await http.PostAsync(RoleAssignmentUrl(externalUserObjectId, roleName), content: null, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task RevokeRoleAsync(string externalUserObjectId, string roleName, CancellationToken ct = default)
    {
        var response = await http.DeleteAsync(RoleAssignmentUrl(externalUserObjectId, roleName), ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<string>> GetAssignedRoleNamesAsync(string externalUserObjectId, CancellationToken ct = default)
    {
        var names = await http.GetFromJsonAsync<List<string>>(
            $"rolesync/users/{Uri.EscapeDataString(externalUserObjectId)}/roles", ct);
        return names ?? [];
    }

    private static string RoleAssignmentUrl(string externalUserObjectId, string roleName) =>
        $"rolesync/users/{Uri.EscapeDataString(externalUserObjectId)}/roles/{Uri.EscapeDataString(roleName)}";
}
