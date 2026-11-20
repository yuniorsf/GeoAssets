using System.Net;
using System.Net.Sockets;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using GeoAssets.Identity.Authentication;

namespace GeoAssets.Gateway;

/// <summary>
/// Resolves the rate limiter's partition key (XD01-214, XD01-393, XD01-398), first match wins:
/// <list type="number">
///   <item><c>org:&lt;value&gt;</c> — the configured organization claim (<see cref="ClaimMapping.OrganizationIdClaimTypes"/>,
///   <c>RateLimiting:OrganizationIdClaimType</c>; none by default, Entra CIAM emits no organization claim);</item>
///   <item><c>sub:&lt;value&gt;</c> — the token's subject, <c>oid</c> then <c>sub</c>;</item>
///   <item><c>ip:&lt;address&gt;</c> — the client address (<see cref="ConnectionInfo.RemoteIpAddress"/>, already rewritten
///   from a trusted proxy's <c>X-Forwarded-For</c> by <see cref="GatewayTrustedProxies"/>), IPv4-mapped IPv6 as IPv4 and
///   IPv6 in its canonical text without a scope id;</item>
///   <item><see cref="UnresolvedPartitionKey"/> — no claim and no client address (an in-process test server).</item>
/// </list>
/// The claims come from <see cref="GatewayTokenValidator"/>: with token validation configured (XD01-398) only a token
/// whose signature, issuer and lifetime validated names a bucket — a forged, expired or unverifiable one keys by the
/// client address, so it can't spend a victim's bucket. Without it (<see cref="GatewayTokenValidationOutcome.Unvalidated"/>)
/// the token is decoded only, as before XD01-398, and a forged one can name another caller's subject or organization.
/// Either way the gateway authorizes nothing (each backend validates the token) and the token itself is never logged. A
/// value longer than <see cref="MaxValueLength"/> is replaced by its SHA-256, so a partition key's length is bounded.
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

    /// <summary>
    /// The decode-only key of <paramref name="request"/> (<see cref="GatewayTokenValidator.DecodeOnly"/>) — the rate
    /// limiter's fallback when <see cref="GatewayRateLimitPartitioning"/> hasn't resolved the caller.
    /// </summary>
    public static string Resolve(HttpRequest request, ClaimMapping claimMapping)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(claimMapping);

        return Resolve(
            GatewayTokenValidator.DecodeOnly(ReadBearerToken(request)),
            claimMapping,
            request.HttpContext.Connection.RemoteIpAddress).Key;
    }

    /// <summary>
    /// The caller of a request whose token is <paramref name="validation"/>: its claims are used only when it is
    /// <see cref="GatewayTokenValidationOutcome.Valid"/> or <see cref="GatewayTokenValidationOutcome.Unvalidated"/>;
    /// otherwise <paramref name="clientAddress"/> decides.
    /// </summary>
    public static PartitionCaller Resolve(GatewayTokenValidation validation, ClaimMapping claimMapping, IPAddress? clientAddress)
    {
        ArgumentNullException.ThrowIfNull(validation);
        ArgumentNullException.ThrowIfNull(claimMapping);

        if (validation.Outcome is GatewayTokenValidationOutcome.Valid or GatewayTokenValidationOutcome.Unvalidated)
        {
            if (FirstClaimValue(validation.Claims, claimMapping.OrganizationIdClaimTypes) is { } organizationId)
                return new PartitionCaller(Bounded("org:", organizationId), "org", validation.Outcome);
            if (FirstClaimValue(validation.Claims, SubjectClaimTypes) is { } subject)
                return new PartitionCaller(Bounded("sub:", subject), "sub", validation.Outcome);
        }

        return ClientAddressKey(clientAddress) is { } addressKey
            ? new PartitionCaller(addressKey, "ip", validation.Outcome)
            : new PartitionCaller(UnresolvedPartitionKey, UnresolvedPartitionKey, validation.Outcome);
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

    /// <summary>The value of <paramref name="request"/>'s <c>Authorization: Bearer …</c> header, trimmed, or <c>null</c> without one.</summary>
    public static string? ReadBearerToken(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var header = request.Headers.Authorization.ToString();
        if (!header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase)) return null;

        var rawToken = header[BearerPrefix.Length..].Trim();
        return rawToken.Length == 0 ? null : rawToken;
    }

    private static string? FirstClaimValue(IReadOnlyList<Claim> claims, IReadOnlyList<string> claimTypes)
    {
        foreach (var claimType in claimTypes)
        {
            var value = claims.FirstOrDefault(c => string.Equals(c.Type, claimType, StringComparison.Ordinal))?.Value;
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }

        return null;
    }

    private static string Bounded(string prefix, string value) =>
        value.Length <= MaxValueLength
            ? prefix + value
            : prefix + "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
