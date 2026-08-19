using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GeoAssets.Identity.Authentication;

/// <summary>
/// Provider-agnostic seam for registering authentication against a specific CIAM/OIDC vendor
/// (XD01-48). Each host's composition root (<c>GeoAssets.Server</c>'s
/// <c>GeoAssetsAuthenticationExtensions</c>, <c>GeoAssets.Web</c>'s
/// <c>GeoAssetsWasmAuthenticationExtensions</c>) accepts an <see cref="IGeoAuthenticationProvider"/>
/// instead of calling a vendor SDK directly (e.g. <c>Microsoft.Identity.Web</c> server-side,
/// <c>Microsoft.Authentication.WebAssembly.Msal</c> client-side) — swapping vendors becomes a
/// DI registration change, not a call-site change. Mirrors how <c>IGeoAuthorizationService</c>
/// already decouples authorization from any specific backend (XD01-13).
///
/// Each host defines its own implementation (server-side bearer-token validation and
/// client-side interactive login are different concerns), so this interface intentionally
/// says nothing about *which* scheme gets registered — only that whatever the implementation
/// registers into <paramref name="services"/>, it reads its own configuration section from
/// <paramref name="configuration"/> rather than assuming a fixed shape at the call site.
/// </summary>
public interface IGeoAuthenticationProvider
{
    void AddAuthentication(IServiceCollection services, IConfiguration configuration);
}
