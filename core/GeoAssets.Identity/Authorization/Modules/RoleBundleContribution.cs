namespace GeoAssets.Identity.Authorization.Modules;

/// <summary>
/// Permissions a module grants to a role (XD01-377). The role is created as a built-in role with
/// <paramref name="RoleName"/> and <paramref name="Description"/> when no role has <paramref name="RoleId"/>; otherwise only
/// the grants missing from it are added. A code that no permission carries is skipped.
/// </summary>
public sealed record RoleBundleContribution(Guid RoleId, string RoleName, string Description, IReadOnlyList<string> PermissionCodes);
