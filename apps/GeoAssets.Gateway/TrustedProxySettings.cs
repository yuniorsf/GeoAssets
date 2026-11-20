using System.Net;

namespace GeoAssets.Gateway;

/// <summary>The proxies and networks whose <c>X-Forwarded-For</c> the gateway believes (XD01-393) — see <see cref="GatewayTrustedProxies"/>.</summary>
internal sealed record TrustedProxySettings(IReadOnlyList<IPAddress> Proxies, IReadOnlyList<IPNetwork> Networks)
{
    public bool IsEmpty => Proxies.Count == 0 && Networks.Count == 0;
}
