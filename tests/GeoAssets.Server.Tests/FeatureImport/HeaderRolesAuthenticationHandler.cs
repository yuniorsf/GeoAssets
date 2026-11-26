using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GeoAssets.Server.Tests.FeatureImport;

internal sealed class HeaderRolesAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "FeatureImportTest";
    public const string RolesHeader = "X-Test-Roles";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(RolesHeader, out var roles))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim> { new("oid", "feature-import-caller"), new(ClaimTypes.NameIdentifier, "feature-import-caller") };
        claims.AddRange(roles.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries).Select(role => new Claim(ClaimTypes.Role, role)));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(
            new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName)), SchemeName)));
    }
}
