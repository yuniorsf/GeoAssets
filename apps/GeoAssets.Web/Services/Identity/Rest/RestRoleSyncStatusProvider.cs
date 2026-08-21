using System.Net.Http.Json;
using GeoAssets.Identity.Authorization.Services;

namespace GeoAssets.Web.Services.Identity.Rest;

/// <summary>
/// <see cref="IRoleSyncStatusProvider"/> backed by <c>GeoAssets.Server</c>'s
/// <c>GET /api/identity/rolesync/status</c> (XD01-63).
/// </summary>
public sealed class RestRoleSyncStatusProvider(HttpClient http) : IRoleSyncStatusProvider
{
    public async Task<bool> IsEnabledAsync(CancellationToken ct = default)
    {
        var dto = await http.GetFromJsonAsync<RoleSyncStatusDto>("rolesync/status", ct);
        return dto?.Enabled ?? false;
    }
}
