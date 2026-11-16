namespace GeoAssets.Identity.Authentication;

/// <summary>
/// Service-identity counterpart to <see cref="IGeoAuthenticationProvider"/> (XD01-299): lets a
/// GeoAssets host acquire a bearer token for itself as a machine caller, so it can call another
/// GeoAssets host (e.g. <c>GeoAssets.Server</c>) authenticated as a service rather than as a
/// human/application caller holding a CIAM session. Each host that needs to call another host
/// registers its own implementation (e.g. an Entra CIAM client-credentials provider) — this
/// interface says nothing about which mechanism is used, only that outbound HTTP clients can ask
/// for a current token without knowing how it's acquired, cached, or refreshed.
/// </summary>
public interface IServiceAuthTokenProvider
{
    /// <summary>
    /// Returns a bearer token valid for calling the target host right now. Implementations are
    /// expected to cache/refresh internally — callers may invoke this on every outbound request.
    /// </summary>
    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default);
}
