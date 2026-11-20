using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GeoAssets.NeutralOperator.Server.Tests.Support;

/// <summary>
/// Authenticates a request by its <c>Authorization: Bearer {token}</c> header against <see cref="TestIdentities.ByToken"/> — the
/// stand-in for the CIAM tenant's JWT validation. The token is also what the host forwards to the kernel's identity routes, so
/// the stubbed kernel answers for the same caller. A request without a known token is not authenticated (401).
/// </summary>
internal sealed class BearerTokenAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "NeutralOperatorTest";

    /// <summary>The claim carrying the caller's organization, which <see cref="NeutralOperatorHost"/> maps as a deployment with an organization claim does.</summary>
    public const string OrganizationClaim = "org";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.Ordinal)
            || !TestIdentities.ByToken.TryGetValue(header["Bearer ".Length..], out var identity))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("oid", identity.UserId.ToString()), new Claim(OrganizationClaim, identity.OrganizationId.ToString())], SchemeName));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }
}
