using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using GeoAssets.Identity.Authentication;

namespace GeoAssets.Gateway;

/// <summary>
/// Resolves the rate limiter's partition key (XD01-214, XD01-393), first match wins:
/// <list type="number">
///   <item><c>org:&lt;value&gt;</c> — the configured organization claim (<see cref="ClaimMapping.OrganizationIdClaimTypes"/>,
///   <c>RateLimiting:OrganizationIdClaimType</c>; none by default, Entra CIAM emits no organization claim);</item>
///   <item><c>sub:&lt;value&gt;</c> — the token's subject, <c>oid</c> then <c>sub</c>;</item>
///   <item><c>ip:&lt;address&gt;</c> — the client address (<see cref="ConnectionInfo.RemoteIpAddress"/>, already rewritten
///   from a trusted proxy's <c>X-Forwarded-For</c> by <see cref="GatewayTrustedProxies"/>), IPv4-mapped IPv6 as IPv4 and
///   IPv6 in its canonical text without a scope id;</item>
///   <item><see cref="UnresolvedPartitionKey"/> — no claim and no client address (an in-process test server).</item>
/// </list>
/// The bearer token is decoded only, never signature-validated: the gateway authorizes nothing (each backend validates the
/// token), so there is no validated principal to read here. A forged or garbage token is harmless for this purpose — it
/// only chooses which bucket its sender lands in, it grants nothing — and the token itself is never logged. A value longer
/// than <see cref="MaxValueLength"/> is replaced by its SHA-256, so a partition key's length is bounded.
/// </summary>
internal static class RateLimitPartitionKeyResolver
{
    /// <summary>The partition every request with no claim and no client address shares.</summary>
    public const string UnresolvedPartitionKey = "unresolved";

    /// <summary>The longest claim value used as is; a longer one is keyed by its SHA-256 (64 hex characters).</summary>
    public const int MaxValueLength = 128;

    private const string BearerPrefix = "Bearer ";

    /// <summary>The token-subject claims, in order: Entra's object id, then the standard JWT subject.</summary>
    private static readonly string[] SubjectClaimTypes = ["oid", "sub"];

    private static readonly JwtSecurityTokenHandler TokenHandler = new();

    public static string Resolve(HttpRequest request, ClaimMapping claimMapping)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(claimMapping);

        if (ReadBearerToken(request) is { } token)
        {
            if (FirstClaimValue(token, claimMapping.OrganizationIdClaimTypes) is { } organizationId)
                return Bounded("org:", organizationId);
            if (FirstClaimValue(token, SubjectClaimTypes) is { } subject)
                return Bounded("sub:", subject);
        }

        return ClientAddressKey(request.HttpContext.Connection.RemoteIpAddress) ?? UnresolvedPartitionKey;
    }

    /// <summary><c>ip:&lt;address&gt;</c> for <paramref name="address"/> normalised (see the type's summary), or <c>null</c> without one.</summary>
    public static string? ClientAddressKey(IPAddress? address)
    {
        if (address is null) return null;

        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();
        else if (address.AddressFamily == AddressFamily.InterNetworkV6 && address.ScopeId != 0)
            address = new IPAddress(address.GetAddressBytes());

        return "ip:" + address;
    }

    private static JwtSecurityToken? ReadBearerToken(HttpRequest request)
    {
        var header = request.Headers.Authorization.ToString();
        if (!header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase)) return null;

        var rawToken = header[BearerPrefix.Length..].Trim();
        if (rawToken.Length == 0 || !TokenHandler.CanReadToken(rawToken)) return null;

        try
        {
            return TokenHandler.ReadJwtToken(rawToken);
        }
        catch (ArgumentException)
        {
            // Shaped like a JWT but not one (undecodable header or payload) — no claims, the client address decides.
            return null;
        }
    }

    private static string? FirstClaimValue(JwtSecurityToken token, IReadOnlyList<string> claimTypes)
    {
        foreach (var claimType in claimTypes)
        {
            var value = token.Claims.FirstOrDefault(c => string.Equals(c.Type, claimType, StringComparison.Ordinal))?.Value;
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }

        return null;
    }

    private static string Bounded(string prefix, string value) =>
        value.Length <= MaxValueLength
            ? prefix + value
            : prefix + "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
