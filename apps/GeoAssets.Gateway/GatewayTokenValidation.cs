using System.Security.Claims;

namespace GeoAssets.Gateway;

/// <summary>
/// A request's bearer token as the gateway sees it (XD01-398): the <paramref name="Outcome"/> of its validation and the
/// <paramref name="Claims"/> it carries (raw JWT claim types; empty unless the outcome is
/// <see cref="GatewayTokenValidationOutcome.Valid"/> or <see cref="GatewayTokenValidationOutcome.Unvalidated"/>).
/// </summary>
internal sealed record GatewayTokenValidation(GatewayTokenValidationOutcome Outcome, IReadOnlyList<Claim> Claims)
{
    /// <summary>No bearer token, or not a JWT.</summary>
    public static readonly GatewayTokenValidation Absent = new(GatewayTokenValidationOutcome.Absent, []);
}
