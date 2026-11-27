namespace GeoAssets.NeutralOperator.Server;

/// <summary>
/// Client-credentials configuration for this host to call the kernel as a <c>GeoAssets.ServiceCaller</c> service principal
/// (XD01-299) — the <c>"ServiceAuth"</c> section. <c>Enabled=false</c> (the default, including when the section is
/// absent) registers no token provider: the kernel's machine-to-machine routes then answer 401, so the bootstrap
/// registration stays pending (Degraded) and ledger reservations answer 503 — the host still boots and serves
/// <c>/healthz</c>. Provisioning the Entra app registration and its secret is an infra step outside this repo.
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

    /// <summary>The kernel API's scope, e.g. <c>"api://geoassets-server/.default"</c>.</summary>
    public string Scope { get; init; } = string.Empty;
}
