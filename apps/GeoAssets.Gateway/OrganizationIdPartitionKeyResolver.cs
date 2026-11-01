using System.IdentityModel.Tokens.Jwt;
using GeoAssets.Identity.Authentication;

namespace GeoAssets.Gateway;

/// <summary>
/// Resolves the rate limiter's partition key (XD01-214) from the caller's bearer token —
/// decoded only, never signature-validated: this gateway doesn't run JWT bearer authentication
/// itself (that happens downstream, at each proxied host — XD01-12/XD01-48), so there is no
/// validated <see cref="System.Security.Claims.ClaimsPrincipal"/> to read here yet. A forged or
/// garbage token is harmless for this specific purpose: it simply lands in its own distinct rate
/// bucket, the same as any other value this method can't resolve a real organization id for —
/// this is a "noisy neighbor" traffic-shaping signal, not an authorization decision.
///
/// Uses the same <see cref="ClaimMapping.EntraDefault"/> claim shape every other host in this
/// solution defaults to (see <c>HttpTenantContext</c> in GeoAssets.Server/
/// GeoAssets.FieldService.Server) — <see cref="ClaimMapping.OrganizationIdClaimTypes"/> is empty
/// there, so until an operator configures a real organization claim for their CIAM tenant, every
/// caller falls into <see cref="UnresolvedPartitionKey"/>'s single shared bucket, same as those
/// hosts' <c>ITenantContext</c> resolving everyone to <see cref="Guid.Empty"/>.
/// </summary>
internal static class OrganizationIdPartitionKeyResolver
{
    /// <summary>The partition every request with no resolvable organization id shares — still rate-limited as one group, just not isolated from each other.</summary>
    public const string UnresolvedPartitionKey = "unresolved";

    private static readonly JwtSecurityTokenHandler TokenHandler = new();

    public static string Resolve(HttpRequest request, ClaimMapping claimMapping)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(claimMapping);

        var header = request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return UnresolvedPartitionKey;

        var rawToken = header["Bearer ".Length..].Trim();
        if (rawToken.Length == 0 || !TokenHandler.CanReadToken(rawToken)) return UnresolvedPartitionKey;

        var token = TokenHandler.ReadJwtToken(rawToken);
        foreach (var claimType in claimMapping.OrganizationIdClaimTypes)
        {
            var value = token.Claims.FirstOrDefault(c => string.Equals(c.Type, claimType, StringComparison.Ordinal))?.Value;
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }

        return UnresolvedPartitionKey;
    }
}
