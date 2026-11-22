namespace GeoAssets.Identity.Authorization.Services;

/// <summary>
/// The current user's cross-organization entitlements (XD01-329) as a client knows them: the grants its organization holds
/// and the roles it holds on other organizations, with those organizations' names (<see cref="CrossOrgEntitlementsDto"/>).
/// The REST-backed client reads them from <c>GET /api/identity/me/cross-org-entitlements</c>. They live as long as the
/// session and are never persisted.
///
/// The server remains the authority: these are for the client to offer what the server will allow, not to decide it.
/// </summary>
public interface ICrossOrgEntitlementsService
{
    /// <summary>The entitlements, fetched on the first call (at sign-in) and kept until <see cref="RefreshAsync"/>.</summary>
    Task<CrossOrgEntitlementsDto> GetAsync(CancellationToken ct = default);

    /// <summary>Fetches the entitlements again (e.g. when the service-order list is reloaded) and keeps the new answer.</summary>
    Task<CrossOrgEntitlementsDto> RefreshAsync(CancellationToken ct = default);
}
