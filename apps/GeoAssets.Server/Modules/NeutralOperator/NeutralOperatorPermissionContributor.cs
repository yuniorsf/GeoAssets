using GeoAssets.Identity.Authorization.Modules;
using GeoAssets.NeutralOperator.Application;

namespace GeoAssets.Server.Modules.NeutralOperator;

/// <summary>
/// The NeutralOperator vertical's role bundle (XD01-377): the <see cref="IspRoles.Tenant"/> role the ISP order types'
/// creation and action policies name (XD01-364) — it submits attachment requests and disputes notices — contributed to
/// the kernel's identity seeding instead of being hard-coded in it. Its permissions are the kernel's own.
/// </summary>
public sealed class NeutralOperatorPermissionContributor : IModulePermissionContributor
{
    /// <summary>The ISP tenant role's well-known id (stable across restarts; seeded by XD01-364 under the same id).</summary>
    public static readonly Guid IspTenantRoleId = new("10000000-0000-0000-0000-000000000005");

    public string VerticalId => NeutralOperatorModule.ModuleId;

    public IEnumerable<PermissionDefinition> Permissions { get; } = [];

    public IEnumerable<RoleBundleContribution> RoleBundles { get; } =
    [
        new(IspTenantRoleId, IspRoles.Tenant, "Inquilino ISP: solicitudes y disputas", ["serviceorders:read", "serviceorders:create"]),
    ];
}
