using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace GeoAssets.Server.Tests.TokenAudiences;

/// <summary>
/// Locally signed Entra-shaped access tokens (XD01-390): an RSA key generated in the test process, a fixed issuer, and the
/// <c>PostConfigure</c> that makes a host's <c>JwtBearer</c> scheme trust exactly that key and issuer without downloading any
/// OpenID metadata. The audience validation is left to whatever the host's own authentication provider configured, so the
/// tests exercise the real audience rules. No real token or secret is involved.
/// </summary>
internal static class LocalJwt
{
    public const string Issuer = "https://tenant.local.test/00000000-0000-4000-8000-000000000390/v2.0";
    public const string KernelAudience = "kernel-api";
    public const string VerticalAudience = "vertical-api";

    private static readonly RsaSecurityKey _key = NewKey();

    public static RsaSecurityKey NewKey() => new(RSA.Create(2048)) { KeyId = Guid.NewGuid().ToString("N") };

    /// <summary>Makes the host's <c>Bearer</c> scheme accept tokens signed by the local key and issued by <see cref="Issuer"/>.</summary>
    public static void TrustLocalSigning(IServiceCollection services) =>
        services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            var configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
            configuration.SigningKeys.Add(_key);
            options.Configuration = configuration;
            options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
            options.RequireHttpsMetadata = false;
            options.MapInboundClaims = false;

            var parameters = options.TokenValidationParameters;
            parameters.IssuerSigningKey = _key;
            parameters.ValidIssuer = Issuer;
            parameters.IssuerValidator = (issuer, _, _) =>
                string.Equals(issuer, Issuer, StringComparison.Ordinal)
                    ? issuer
                    : throw new SecurityTokenInvalidIssuerException($"The issuer '{issuer}' is invalid.");
            parameters.RoleClaimType = "roles";
        });

    /// <summary>The validation of a vertical host that accepts only <paramref name="audience"/> (its own API registration).</summary>
    public static TokenValidationParameters ValidationFor(string audience) => new()
    {
        ValidIssuer = Issuer,
        ValidAudience = audience,
        IssuerSigningKey = _key,
        RoleClaimType = "roles",
    };

    /// <summary>A v2.0 access token for <paramref name="audience"/> carrying the given roles and permissions.</summary>
    public static string Mint(
        string audience,
        IEnumerable<string>? roles = null,
        IEnumerable<string>? permissions = null,
        bool expired = false,
        string? issuer = null,
        SecurityKey? signingKey = null)
    {
        var now = TimeProvider.System.GetUtcNow().UtcDateTime;
        var claims = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["ver"] = "2.0",
            ["scp"] = "access",
            ["oid"] = "00000000-0000-4000-8000-000000000a90",
            ["roles"] = (roles ?? []).ToArray(),
            ["perm"] = (permissions ?? []).ToArray(),
        };

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer ?? Issuer,
            Audience = audience,
            NotBefore = expired ? now.AddHours(-3) : now.AddMinutes(-1),
            Expires = expired ? now.AddHours(-2) : now.AddHours(1),
            Claims = claims,
            SigningCredentials = new SigningCredentials(signingKey ?? _key, SecurityAlgorithms.RsaSha256),
        });
    }
}
