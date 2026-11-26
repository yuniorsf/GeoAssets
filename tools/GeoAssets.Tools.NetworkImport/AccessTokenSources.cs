namespace GeoAssets.Tools.NetworkImport;

/// <summary>Chooses the token source from the environment: a static token, else client credentials.</summary>
internal static class AccessTokenSources
{
    public const string TokenVariable = "GEOASSETS_NETWORK_IMPORT_TOKEN";
    public const string InstanceVariable = "GEOASSETS_SERVICEAUTH_INSTANCE";
    public const string TenantIdVariable = "GEOASSETS_SERVICEAUTH_TENANT_ID";
    public const string ClientIdVariable = "GEOASSETS_SERVICEAUTH_CLIENT_ID";
    public const string ClientSecretVariable = "GEOASSETS_SERVICEAUTH_CLIENT_SECRET";
    public const string ScopeVariable = "GEOASSETS_SERVICEAUTH_SCOPE";

    public static (IAccessTokenSource? Source, string? Error) FromEnvironment(Func<string, string?> getEnvironmentVariable)
    {
        ArgumentNullException.ThrowIfNull(getEnvironmentVariable);
        var token = getEnvironmentVariable(TokenVariable);
        if (!string.IsNullOrWhiteSpace(token))
            return (new StaticAccessTokenSource(token.Trim()), null);

        var instance = getEnvironmentVariable(InstanceVariable);
        var tenantId = getEnvironmentVariable(TenantIdVariable);
        var clientId = getEnvironmentVariable(ClientIdVariable);
        var clientSecret = getEnvironmentVariable(ClientSecretVariable);
        var scope = getEnvironmentVariable(ScopeVariable);
        if (string.IsNullOrWhiteSpace(instance) || string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(clientId)
            || string.IsNullOrWhiteSpace(clientSecret) || string.IsNullOrWhiteSpace(scope))
        {
            return (null, $"Set {TokenVariable}, or {InstanceVariable}, {TenantIdVariable}, {ClientIdVariable}, {ClientSecretVariable} and {ScopeVariable}.");
        }

        var authority = CiamAuthority(instance, tenantId);
        if (authority is null)
            return (null, $"{InstanceVariable} must be an absolute https URL, the kernel's AzureAdCiam:Instance (https://<tenant>.ciamlogin.com/).");

        return (new ClientCredentialsAccessTokenSource(authority, clientId, clientSecret, scope), null);
    }

    // The kernel accepts only the issuer of its CIAM instance (https://<tenant id>.ciamlogin.com/<tenant id>/v2.0); a token
    // asked from login.microsoftonline.com — MSAL's WithTenantId default — carries that host's issuer and is refused (401).
    private static Uri? CiamAuthority(string instance, string tenantId)
    {
        if (!Uri.TryCreate(instance.Trim(), UriKind.Absolute, out var root) || !string.Equals(root.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal))
            return null;
        return new Uri($"{root.GetLeftPart(UriPartial.Authority)}/{Uri.EscapeDataString(tenantId.Trim())}", UriKind.Absolute);
    }
}
