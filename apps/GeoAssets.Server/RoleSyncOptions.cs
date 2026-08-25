namespace GeoAssets.Server;

/// <summary>
/// Binds the <c>"RoleSync"</c> configuration section (XD01-59 Phase 2) — the Graph automation
/// credential provisioned per <c>RoleSyncAzureSetup.md</c> (XD01-60), plus which app
/// registrations <see cref="EntraGraphRoleAssignmentProvider"/> targets. <see cref="ClientSecret"/>
/// must never live in a tracked appsettings.json — see the project's public-repo CIAM secrets
/// convention (<c>dotnet user-secrets</c> locally, the deployed secret store in production).
/// </summary>
public sealed class RoleSyncOptions
{
    /// <summary>
    /// Master switch. When false (the default), <see cref="GeoAssetsRoleAssignmentExtensions.AddRoleAssignmentProvider"/>
    /// registers <see cref="GeoAssets.Identity.Authorization.Services.NullRoleAssignmentProvider"/>
    /// instead — Phase 1's admin UI is unaffected regardless of what else is configured here.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>Tenant ID of the GeoAssets Entra External ID (CIAM) tenant.</summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Client (Application) ID of the "GeoAssets Role Sync" app registration itself.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>Client secret for the above — never checked into source control.</summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// Client (Application) IDs of the app registration(s) a role must be registered/assigned
    /// against — typically both GeoAssets Web and GeoAssets Server, since which one issues the
    /// token GeoAssets reads the <c>roles</c> claim from depends on <c>Identity:Backend</c>.
    /// </summary>
    public string[] TargetApplicationClientIds { get; set; } = [];
}
