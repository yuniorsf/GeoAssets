using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GeoAssets.Server.Tests.IpBillingBridge;

/// <summary>
/// Stands in for the CIAM JWT validation of a host: a request's <c>Authorization: Bearer {token}</c> is looked up in
/// <see cref="BridgeCallers.ByToken"/>; an unknown token or none leaves the request unauthenticated. The stand-in kernel only
/// accepts the tokens that are <see cref="BridgeCaller.KernelAcceptsToken"/>, the IpBilling host accepts every known one —
/// the audience difference the ADR calls a risk.
/// </summary>
internal sealed class BridgeTokenAuthenticationHandler(
    IOptionsMonitor<BridgeTokenAuthenticationOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<BridgeTokenAuthenticationOptions>(options, logger, encoder)
{
    public const string SchemeName = "BridgeTest";

    public const string PermissionClaim = "perm";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.Ordinal)
            || !BridgeCallers.ByToken.TryGetValue(header["Bearer ".Length..], out var caller)
            || (Options.KernelOnly && !caller.KernelAcceptsToken))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim> { new("oid", caller.Subject), new(ClaimTypes.NameIdentifier, caller.Subject) };
        claims.AddRange(caller.Roles.Select(r => new Claim(ClaimTypes.Role, r)));
        claims.AddRange(caller.Permissions.Select(p => new Claim(PermissionClaim, p)));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName)), SchemeName)));
    }
}
