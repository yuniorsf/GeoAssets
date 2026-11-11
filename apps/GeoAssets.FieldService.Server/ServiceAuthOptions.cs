namespace GeoAssets.FieldService.Server;

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

    public string TenantId { get; init; } = string.Empty;

    public string ClientId { get; init; } = string.Empty;

    public string ClientSecret { get; init; } = string.Empty;

    /// <summary>The target API's scope, e.g. <c>"api://geoassets-server/.default"</c>.</summary>
    public string Scope { get; init; } = string.Empty;
}
