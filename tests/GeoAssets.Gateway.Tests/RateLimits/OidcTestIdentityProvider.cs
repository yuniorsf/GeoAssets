using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace GeoAssets.Gateway.Tests.RateLimits;

/// <summary>
/// A fake Entra CIAM tenant (XD01-398): one signing key, an OIDC metadata document and a JWKS served by a counting
/// <see cref="HttpMessageHandler"/>, and a minter of tokens signed by that key or by another one.
/// </summary>
internal sealed class OidcTestIdentityProvider : IDisposable
{
    public const string MetadataAddress = "http://ciam.test/tenant/v2.0/.well-known/openid-configuration";
    public const string Issuer = "https://ciam.test/tenant/v2.0";
    private const string JwksAddress = "http://ciam.test/tenant/discovery/v2.0/keys";

    private readonly RSA _rsa = RSA.Create(2048);
    private readonly RSA _foreignRsa = RSA.Create(2048);

    public OidcTestIdentityProvider() => Handler = new CountingHandler(this);

    public CountingHandler Handler { get; }

    public void Dispose()
    {
        _rsa.Dispose();
        _foreignRsa.Dispose();
        Handler.Dispose();
    }

    public HttpClient CreateHttpClient() => new(Handler, disposeHandler: false);

    /// <summary>A token with <paramref name="claims"/>, valid for an hour from <paramref name="now"/> unless the times are overridden.</summary>
    public string Mint(
        DateTimeOffset now,
        IReadOnlyDictionary<string, object> claims,
        string issuer = Issuer,
        TimeSpan? notBefore = null,
        TimeSpan? expires = null,
        bool foreignKey = false,
        string? audience = null,
        bool reuseKeyId = false)
    {
        ArgumentNullException.ThrowIfNull(claims);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience ?? "api://gateway-test",
            Claims = new Dictionary<string, object>(claims, StringComparer.Ordinal),
            NotBefore = (now + (notBefore ?? TimeSpan.Zero)).UtcDateTime,
            Expires = (now + (expires ?? TimeSpan.FromHours(1))).UtcDateTime,
            IssuedAt = now.UtcDateTime,
            SigningCredentials = new SigningCredentials(
                new RsaSecurityKey(foreignKey ? _foreignRsa : _rsa) { KeyId = foreignKey && !reuseKeyId ? "foreign-key" : "test-key" },
                SecurityAlgorithms.RsaSha256),
        };
        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    public string Mint(DateTimeOffset now, string subject, bool foreignKey = false, TimeSpan? notBefore = null, TimeSpan? expires = null, string issuer = Issuer, string? audience = null, bool reuseKeyId = false) =>
        Mint(now, new Dictionary<string, object>(StringComparer.Ordinal) { ["oid"] = subject }, issuer, notBefore, expires, foreignKey, audience, reuseKeyId);

    private static string MetadataJson() =>
        JsonSerializer.Serialize(new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["issuer"] = Issuer,
            ["jwks_uri"] = JwksAddress,
        });

    private string JwksJson()
    {
        var parameters = _rsa.ExportParameters(includePrivateParameters: false);
        return JsonSerializer.Serialize(new
        {
            keys = new[]
            {
                new
                {
                    kty = "RSA",
                    use = "sig",
                    kid = "test-key",
                    alg = "RS256",
                    n = Base64UrlEncoder.Encode(parameters.Modulus),
                    e = Base64UrlEncoder.Encode(parameters.Exponent),
                },
            },
        });
    }

    internal sealed class CountingHandler(OidcTestIdentityProvider owner) : HttpMessageHandler
    {
        private int _metadataFetches;
        private int _jwksFetches;

        public int MetadataFetches => Volatile.Read(ref _metadataFetches);

        public int JwksFetches => Volatile.Read(ref _jwksFetches);

        public int TotalFetches => MetadataFetches + JwksFetches;

        /// <summary>While set, every fetch fails like an unreachable tenant (and is still counted).</summary>
        public volatile bool Down;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();

            var path = request.RequestUri!.AbsolutePath;
            var isMetadata = path.EndsWith("openid-configuration", StringComparison.Ordinal);
            if (isMetadata) Interlocked.Increment(ref _metadataFetches);
            else Interlocked.Increment(ref _jwksFetches);

            if (Down) throw new HttpRequestException("the tenant is unreachable");

            var body = isMetadata ? MetadataJson() : owner.JwksJson();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }
}
