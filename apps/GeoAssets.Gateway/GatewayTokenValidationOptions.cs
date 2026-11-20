using System.Globalization;

namespace GeoAssets.Gateway;

/// <summary>
/// How the gateway validates a bearer token before keying a rate-limit bucket by its claims (XD01-398).
/// <paramref name="MetadataAddress"/> is the Entra CIAM tenant's OpenID Connect metadata document (its issuer and signing
/// keys) — <c>AzureAdCiam:MetadataAddress</c>, else <c>{AzureAdCiam:Instance}/{AzureAdCiam:TenantId}/v2.0/.well-known/openid-configuration</c>;
/// none of them set means validation is not configured and the claims are only decoded (the pre-XD01-398 behaviour).
/// <paramref name="ClockSkew"/> (<c>RateLimiting:TokenValidation:ClockSkewSeconds</c>) is the lifetime tolerance;
/// <paramref name="MetadataFailureBackoff"/> (<c>RateLimiting:TokenValidation:MetadataFailureBackoffSeconds</c>) is how long
/// after a failed metadata fetch the gateway keys by the client address without trying again.
/// </summary>
internal sealed record GatewayTokenValidationOptions(string? MetadataAddress, TimeSpan ClockSkew, TimeSpan MetadataFailureBackoff)
{
    public static readonly TimeSpan DefaultClockSkew = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan DefaultMetadataFailureBackoff = TimeSpan.FromSeconds(30);

    private const string CiamSection = "AzureAdCiam";
    private const string TokenValidationSection = "RateLimiting:TokenValidation";
    private const string ClockSkewKey = "ClockSkewSeconds";
    private const string MetadataFailureBackoffKey = "MetadataFailureBackoffSeconds";
    private const string MetadataPathSuffix = "/v2.0/.well-known/openid-configuration";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(MetadataAddress);

    /// <summary>
    /// Reads the options; throws at startup when a duration is not a whole number of seconds of at least 1, or the
    /// metadata address is not an absolute http(s) URI.
    /// </summary>
    public static GatewayTokenValidationOptions Read(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var ciam = configuration.GetSection(CiamSection);
        var metadataAddress = MetadataAddressOf(ciam["MetadataAddress"], ciam["Instance"], ciam["TenantId"]);
        if (metadataAddress is not null
            && (!Uri.TryCreate(metadataAddress, UriKind.Absolute, out var uri) || (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal) && !string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal))))
        {
            throw new InvalidOperationException(
                $"Configuration '{CiamSection}' gives the OpenID Connect metadata address '{metadataAddress}', which is not an absolute http(s) URI (set {CiamSection}:MetadataAddress, or {CiamSection}:Instance and {CiamSection}:TenantId).");
        }

        var validation = configuration.GetSection(TokenValidationSection);
        return new GatewayTokenValidationOptions(
            metadataAddress,
            Seconds(validation, ClockSkewKey, DefaultClockSkew),
            Seconds(validation, MetadataFailureBackoffKey, DefaultMetadataFailureBackoff));
    }

    private static string? MetadataAddressOf(string? metadataAddress, string? instance, string? tenantId)
    {
        if (!string.IsNullOrWhiteSpace(metadataAddress)) return metadataAddress.Trim();
        if (string.IsNullOrWhiteSpace(instance) || string.IsNullOrWhiteSpace(tenantId)) return null;

        return instance.Trim().TrimEnd('/') + "/" + tenantId.Trim() + MetadataPathSuffix;
    }

    private static TimeSpan Seconds(IConfiguration section, string key, TimeSpan fallback)
    {
        var raw = section[key];
        if (string.IsNullOrWhiteSpace(raw)) return fallback;

        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds) || seconds < 1)
        {
            throw new InvalidOperationException(
                $"Configuration '{TokenValidationSection}:{key}' must be a whole number of seconds of at least 1 (got '{raw}').");
        }

        return TimeSpan.FromSeconds(seconds);
    }
}
