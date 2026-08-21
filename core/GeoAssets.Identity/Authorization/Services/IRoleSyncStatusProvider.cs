namespace GeoAssets.Identity.Authorization.Services;

/// <summary>
/// Reports whether the server-side <see cref="IRoleAssignmentProvider"/> is really Graph-backed
/// (vs. the no-op <see cref="NullRoleAssignmentProvider"/> default) — XD01-63. A client-side
/// consumer can't tell which concrete provider the server resolved just by having a working
/// <see cref="IRoleAssignmentProvider"/> reference (under <c>Identity:Backend=Rest</c>, the
/// client always resolves a working HTTP-backed proxy regardless of whether the server's own
/// <c>RoleSync:Enabled</c> flag is on), so the admin UI asks this instead before showing
/// "Register in Entra"/"Assign in Entra" controls — matching the "hide entirely rather than
/// show a broken/no-op button" requirement.
///
/// Not registered at all under <c>Identity:Backend=InMemory</c> (no server round-trip exists in
/// that mode, so role sync can never be functional there) — callers resolve it optionally, the
/// same pattern already used for <c>UserProvisioningService</c>.
/// </summary>
public interface IRoleSyncStatusProvider
{
    Task<bool> IsEnabledAsync(CancellationToken ct = default);
}
