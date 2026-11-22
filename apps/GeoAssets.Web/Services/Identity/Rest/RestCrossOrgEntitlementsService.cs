using System.Net.Http.Json;
using GeoAssets.Identity.Authorization.Services;

namespace GeoAssets.Web.Services.Identity.Rest;

/// <summary>
/// HTTP-backed <see cref="ICrossOrgEntitlementsService"/> (XD01-329): <c>GET me/cross-org-entitlements</c> on the server's
/// <c>/api/identity/</c> client. Fetched on first use — at sign-in, when <see cref="RestGeoAuthorizationService"/> first
/// builds the caller's <see cref="AuthorizationContext"/> — and again on <see cref="RefreshAsync"/>; held in memory for the
/// session only, never in local storage.
///
/// A server that does not answer the route (an older kernel, or any non-success status) yields
/// <see cref="CrossOrgEntitlementsDto.None"/>: the client then offers less than the server would allow, never more.
/// </summary>
public sealed class RestCrossOrgEntitlementsService(HttpClient http) : ICrossOrgEntitlementsService
{
    private static readonly Uri Route = new("me/cross-org-entitlements", UriKind.Relative);

    private Task<CrossOrgEntitlementsDto>? _current;

    public Task<CrossOrgEntitlementsDto> GetAsync(CancellationToken ct = default) => _current ??= FetchAsync(ct);

    public Task<CrossOrgEntitlementsDto> RefreshAsync(CancellationToken ct = default) => _current = FetchAsync(ct);

    private async Task<CrossOrgEntitlementsDto> FetchAsync(CancellationToken ct)
    {
        using var response = await http.GetAsync(Route, ct);
        if (!response.IsSuccessStatusCode) return CrossOrgEntitlementsDto.None;

        return await response.Content.ReadFromJsonAsync<CrossOrgEntitlementsDto>(ct) ?? CrossOrgEntitlementsDto.None;
    }
}
