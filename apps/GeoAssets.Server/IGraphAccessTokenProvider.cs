namespace GeoAssets.Server;

/// <summary>
/// Testable seam between <see cref="EntraGraphRoleAssignmentProvider"/> and MSAL.NET's
/// client-credentials token acquisition — lets tests supply a fixed token without a live
/// credential or network call (see <see cref="MsalGraphAccessTokenProvider"/> for the real
/// implementation).
/// </summary>
public interface IGraphAccessTokenProvider
{
    Task<string> GetAccessTokenAsync(CancellationToken ct = default);
}
