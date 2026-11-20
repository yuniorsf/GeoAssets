using System.IdentityModel.Tokens.Jwt;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace GeoAssets.Gateway;

/// <summary>
/// Validates a request's bearer token for the rate limiter's partition key (XD01-398) — only so a bucket is keyed by a
/// subject or organization the token's issuer vouches for; the gateway still authorizes nothing and never rejects a
/// request because of its token.
/// <para>
/// Configured (<see cref="GatewayTokenValidationOptions.IsConfigured"/>): the token's signature, issuer and lifetime are
/// validated against the Entra CIAM tenant's OpenID Connect metadata, read through the cached
/// <see cref="IConfigurationManager{T}"/> (its own refresh schedule; an unknown <c>kid</c> asks it for a refresh, which it
/// bounds by its <c>RefreshInterval</c>). The audience is not validated: the gateway fronts several APIs, each validating
/// its own. A failed metadata fetch makes every token <see cref="GatewayTokenValidationOutcome.Unavailable"/> for
/// <see cref="GatewayTokenValidationOptions.MetadataFailureBackoff"/> without trying again. Not configured: the token is
/// decoded only (<see cref="GatewayTokenValidationOutcome.Unvalidated"/>), as before XD01-398.
/// </para>
/// Never logs the token nor a claim value — only the outcome and the failure's exception type.
/// </summary>
internal sealed partial class GatewayTokenValidator
{
    /// <summary>The named <see cref="HttpClient"/> the metadata document and signing keys are fetched with.</summary>
    public const string MetadataHttpClientName = "gateway-oidc-metadata";

    /// <summary>Decodes only — no signature, issuer nor lifetime check.</summary>
    private static readonly JwtSecurityTokenHandler DecodingHandler = new();

    private static readonly GatewayTokenValidation Unavailable = new(GatewayTokenValidationOutcome.Unavailable, []);
    private static readonly GatewayTokenValidation Invalid = new(GatewayTokenValidationOutcome.Invalid, []);

    private readonly GatewayTokenValidationOptions _options;
    private readonly IConfigurationManager<OpenIdConnectConfiguration>? _configurationManager;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<GatewayTokenValidator> _logger;
    private readonly JsonWebTokenHandler _validatingHandler = new() { MapInboundClaims = false };

    /// <summary>UTC ticks of the last failed metadata fetch; 0 while the last fetch succeeded (or none failed).</summary>
    private long _metadataFailedAtUtcTicks;

    /// <param name="options">The validation options.</param>
    /// <param name="configurationManager">The CIAM metadata source — required when <paramref name="options"/> is configured, <c>null</c> otherwise.</param>
    /// <param name="timeProvider">The clock of the lifetime check and of the metadata failure backoff.</param>
    /// <param name="logger">Debug outcomes, metadata fetch failures.</param>
    public GatewayTokenValidator(
        GatewayTokenValidationOptions options,
        IConfigurationManager<OpenIdConnectConfiguration>? configurationManager,
        TimeProvider timeProvider,
        ILogger<GatewayTokenValidator> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);
        if (options.IsConfigured != configurationManager is not null)
        {
            throw new ArgumentException(
                "A configuration manager is required exactly when token validation is configured.", nameof(configurationManager));
        }

        _options = options;
        _configurationManager = configurationManager;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>Whether tokens are validated (else only decoded).</summary>
    public bool IsConfigured => _configurationManager is not null;

    /// <summary>
    /// The decode-only view of <paramref name="rawToken"/>: <see cref="GatewayTokenValidationOutcome.Unvalidated"/> with its
    /// claims, or <see cref="GatewayTokenValidation.Absent"/> when there is no token or it isn't a decodable JWT.
    /// </summary>
    public static GatewayTokenValidation DecodeOnly(string? rawToken)
    {
        if (string.IsNullOrWhiteSpace(rawToken) || !DecodingHandler.CanReadToken(rawToken)) return GatewayTokenValidation.Absent;

        try
        {
            return new GatewayTokenValidation(GatewayTokenValidationOutcome.Unvalidated, DecodingHandler.ReadJwtToken(rawToken).Claims.ToList());
        }
        catch (ArgumentException)
        {
            // Shaped like a JWT but not one (undecodable header or payload) — no claims, the client address decides.
            return GatewayTokenValidation.Absent;
        }
    }

    /// <summary>
    /// <paramref name="rawToken"/> (the bearer value; <c>null</c>/blank → <see cref="GatewayTokenValidation.Absent"/>)
    /// validated, or only decoded when validation is not configured. Never throws, except
    /// <see cref="OperationCanceledException"/> for <paramref name="cancellationToken"/>.
    /// </summary>
    public async Task<GatewayTokenValidation> ValidateAsync(string? rawToken, CancellationToken cancellationToken)
    {
        var decoded = DecodeOnly(rawToken);
        if (_configurationManager is null || decoded.Outcome == GatewayTokenValidationOutcome.Absent) return decoded;

        if (IsBackingOff())
        {
            LogOutcome(_logger, GatewayTokenValidationOutcome.Unavailable, "MetadataBackoff");
            return Unavailable;
        }

        OpenIdConnectConfiguration configuration;
        try
        {
            configuration = await _configurationManager.GetConfigurationAsync(cancellationToken).ConfigureAwait(false);
            Volatile.Write(ref _metadataFailedAtUtcTicks, 0);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            Volatile.Write(ref _metadataFailedAtUtcTicks, _timeProvider.GetUtcNow().UtcTicks);
            if (_logger.IsEnabled(LogLevel.Warning))
                LogMetadataUnavailable(_logger, ex.GetType().Name, (int)_options.MetadataFailureBackoff.TotalSeconds, ex);
            return Unavailable;
        }

        TokenValidationResult result;
        try
        {
            result = await _validatingHandler.ValidateTokenAsync(rawToken, ParametersFor(configuration)).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogInvalid(ex);
            return Invalid;
        }

        if (result.IsValid)
        {
            LogOutcome(_logger, GatewayTokenValidationOutcome.Valid, "None");
            return new GatewayTokenValidation(GatewayTokenValidationOutcome.Valid, result.ClaimsIdentity.Claims.ToList());
        }

        // A key the cached metadata doesn't have: the tenant may have rolled its keys. The manager refetches on its next
        // read, at most once per its RefreshInterval, so a stream of forged kids can't make the gateway hammer the tenant.
        if (result.Exception is SecurityTokenSignatureKeyNotFoundException)
            _configurationManager.RequestRefresh();

        LogInvalid(result.Exception);
        return Invalid;
    }

    private bool IsBackingOff()
    {
        var failedAt = Volatile.Read(ref _metadataFailedAtUtcTicks);
        return failedAt != 0 && _timeProvider.GetUtcNow().UtcTicks - failedAt < _options.MetadataFailureBackoff.Ticks;
    }

    private TokenValidationParameters ParametersFor(OpenIdConnectConfiguration configuration) => new()
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKeys = configuration.SigningKeys,
        RequireSignedTokens = true,
        ValidateIssuer = true,
        ValidIssuer = configuration.Issuer,
        ValidateLifetime = true,
        RequireExpirationTime = true,
        ClockSkew = _options.ClockSkew,
        LifetimeValidator = IsWithinLifetime,
        AudienceValidator = AcceptsAudience,
    };

    /// <summary>
    /// The lifetime check on <see cref="_timeProvider"/>'s clock (the handler's own reads the system clock): an expiry is
    /// required, and now must lie within [<paramref name="notBefore"/> − skew, <paramref name="expires"/> + skew].
    /// </summary>
    private bool IsWithinLifetime(DateTime? notBefore, DateTime? expires, SecurityToken securityToken, TokenValidationParameters validationParameters)
    {
        if (expires is not { } expiresAt) return false;

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var expiresUtc = expiresAt.ToUniversalTime();
        if (notBefore is { } notBeforeAt)
        {
            var notBeforeUtc = notBeforeAt.ToUniversalTime();
            if (notBeforeUtc > expiresUtc || notBeforeUtc > now + _options.ClockSkew) return false;
        }

        return expiresUtc >= now - _options.ClockSkew;
    }

    /// <summary>
    /// Any audience the token names, but it must name one: the gateway fronts several APIs (the kernel and each vertical
    /// host), each with its own audience and each validating it itself, so a token for any of them identifies its caller
    /// equally well for a bucket — the token grants nothing here. A token without an <c>aud</c> is no access token
    /// (RFC 9068 §2.2; every token Entra issues carries one), so it doesn't name a bucket.
    /// </summary>
    private static bool AcceptsAudience(IEnumerable<string> audiences, SecurityToken securityToken, TokenValidationParameters validationParameters) =>
        audiences.Any(audience => !string.IsNullOrWhiteSpace(audience));

    private void LogInvalid(Exception? failure)
    {
        if (!_logger.IsEnabled(LogLevel.Debug)) return;

        var failureKind = failure is null ? "Unknown" : failure.GetType().Name;
        LogOutcome(_logger, GatewayTokenValidationOutcome.Invalid, failureKind);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Rate-limit token validation: {Outcome} ({FailureKind}).")]
    private static partial void LogOutcome(ILogger logger, GatewayTokenValidationOutcome outcome, string failureKind);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rate-limit token validation: the CIAM signing keys could not be fetched ({FailureKind}); tokens key by the client address for {BackoffSeconds} s before the next attempt.")]
    private static partial void LogMetadataUnavailable(ILogger logger, string failureKind, int backoffSeconds, Exception exception);
}
