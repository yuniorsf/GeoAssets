using System.Net.Http.Json;
using GeoAssets.Identity.Authorization.Services;

namespace GeoAssets.Web.Services.Identity.Rest;

/// <summary>
/// <see cref="IInvitationStatusProvider"/> backed by <c>GeoAssets.Server</c>'s
/// <c>GET /api/identity/invitations/status</c> (XD01-69).
/// </summary>
public sealed class RestInvitationStatusProvider(HttpClient http) : IInvitationStatusProvider
{
    public async Task<bool> IsEnabledAsync(CancellationToken ct = default)
    {
        var dto = await http.GetFromJsonAsync<InvitationStatusDto>("invitations/status", ct);
        return dto?.Enabled ?? false;
    }
}
