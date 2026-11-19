namespace GeoAssets.Identity.Authorization.Modules;

/// <summary>
/// The chassis contract by which a vertical publishes its permissions and role bundles (XD01-377, ADR-001 smell L), so
/// the kernel's identity seeder names no vertical's codes. In process, every registered contributor is applied at startup
/// with the kernel's own seeding; out of process, a vertical PUTs the same <see cref="ModulePermissionSet"/>. Both are
/// idempotent upserts. Removing a contributor deletes nothing: the permissions and grants it seeded stay until an
/// administrator removes them.
/// </summary>
public interface IModulePermissionContributor
{
    /// <summary>The contributing vertical, e.g. <c>ip-billing</c>.</summary>
    string VerticalId { get; }

    IEnumerable<PermissionDefinition> Permissions { get; }

    IEnumerable<RoleBundleContribution> RoleBundles { get; }
}
