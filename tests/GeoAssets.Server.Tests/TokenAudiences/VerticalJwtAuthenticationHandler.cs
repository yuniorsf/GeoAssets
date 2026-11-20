using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;

namespace GeoAssets.Server.Tests.TokenAudiences;

/// <summary>
/// Stands in for a vertical host's CIAM validation: a bearer token is validated for real (signature, issuer, lifetime) against the
/// local key, accepting only <see cref="LocalJwt.VerticalAudience"/> — the vertical's own API registration — and the principal
/// carries the token's claims (<c>aud</c> and <c>iss</c> included), as a validated JwtBearer principal does.
/// </summary>
internal sealed class VerticalJwtAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "VerticalJwt";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.Ordinal)) return AuthenticateResult.NoResult();

        var result = await new JsonWebTokenHandler()
            .ValidateTokenAsync(header["Bearer ".Length..], LocalJwt.ValidationFor(LocalJwt.VerticalAudience))
            .ConfigureAwait(false);
        if (!result.IsValid) return AuthenticateResult.Fail(result.Exception ?? new InvalidOperationException("The token is not valid."));

        var identity = new ClaimsIdentity(result.ClaimsIdentity.Claims, SchemeName, "name", "roles");
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }
}
