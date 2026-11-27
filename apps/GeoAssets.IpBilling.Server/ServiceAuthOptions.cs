namespace GeoAssets.IpBilling.Server;

/// <summary>
/// Client-credentials configuration for this host to authenticate itself as a service caller
/// (XD01-299) — the <c>"ServiceAuth"</c> section in appsettings.json. <c>Enabled=false</c>
/// (the default, including when the section is absent) means this host calls other GeoAssets
/// hosts the same way it did before this ticket: no bearer token attached. Provisioning the
/// real Entra app registration/client secret this binds to is an infra/ops step outside this
/// repo — see <see cref="GeoAssetsServiceAuthExtensions"/>.
/// </summary>
internal sealed record ServiceAuthOptions
{
    public bool Enabled { get; init; }

    /// <summary>
    /// The CIAM instance tokens are asked from, e.g. <c>"https://&lt;tenant&gt;.ciamlogin.com/"</c> (XD01-452); blank means
    /// <c>AzureAdCiam:Instance</c>. The authority is this instance followed by <see cref="TenantId"/>: the kernel accepts
    /// only its CIAM instance's issuer.
    /// </summary>
    public string Instance { get; init; } = string.Empty;

    public string TenantId { get; init; } = string.Empty;

    public string ClientId { get; init; } = string.Empty;

    public string ClientSecret { get; init; } = string.Empty;

    /// <summary>The target API's scope, e.g. <c>"api://geoassets-server/.default"</c>.</summary>
    public string Scope { get; init; } = string.Empty;
}
