namespace GeoAssets.Tools.NetworkImport;

/// <summary>Chooses the token source from the environment: a static token, else client credentials.</summary>
internal static class AccessTokenSources
{
    public const string TokenVariable = "GEOASSETS_NETWORK_IMPORT_TOKEN";
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

        var tenantId = getEnvironmentVariable(TenantIdVariable);
        var clientId = getEnvironmentVariable(ClientIdVariable);
        var clientSecret = getEnvironmentVariable(ClientSecretVariable);
        var scope = getEnvironmentVariable(ScopeVariable);
        if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(clientId)
            || string.IsNullOrWhiteSpace(clientSecret) || string.IsNullOrWhiteSpace(scope))
        {
            return (null, $"Set {TokenVariable}, or {TenantIdVariable}, {ClientIdVariable}, {ClientSecretVariable} and {ScopeVariable}.");
        }

        return (new ClientCredentialsAccessTokenSource(tenantId, clientId, clientSecret, scope), null);
    }
}
