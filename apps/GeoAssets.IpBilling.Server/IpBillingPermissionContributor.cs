using GeoAssets.Identity.Authorization.Modules;

namespace GeoAssets.IpBilling.Server;

/// <summary>
/// The IP-billing vertical's permissions (XD01-377) — XD01-176's reference-data and Levantamento/Fiscalização/Ofício
/// permissions. Seeded by the kernel's identity seeding while the kernel hosted the IP-billing bridge module; published by
/// this host's bootstrap registration (<c>PUT /api/identity/verticals/ip-billing/permissions</c>) since XD01-385. No role
/// bundles: the role wiring was left to the phase that first needs these permissions, so none is granted here either.
/// </summary>
internal sealed class IpBillingPermissionContributor : IModulePermissionContributor
{
    /// <summary>This vertical's id at the kernel's registration routes.</summary>
    public const string IpBillingVerticalId = "ip-billing";

    public string VerticalId => IpBillingVerticalId;

    public IEnumerable<PermissionDefinition> Permissions { get; } =
    [
        new("ip-billing:read",             "ip-billing", "read",             "permissions.ipBilling.read"),
        new("ip-billing:upload",           "ip-billing", "upload",           "permissions.ipBilling.upload"),
        new("ip-billing:calculate",        "ip-billing", "calculate",        "permissions.ipBilling.calculate"),
        new("ip-billing:validate",         "ip-billing", "validate",         "permissions.ipBilling.validate"),
        new("ip-billing:ledger-manual",    "ip-billing", "ledger-manual",    "permissions.ipBilling.ledgerManual"),
        new("ip-billing:ledger-recarga",   "ip-billing", "ledger-recarga",   "permissions.ipBilling.ledgerRecarga"),
        new("ip-billing:reference-manage", "ip-billing", "reference-manage", "permissions.ipBilling.referenceManage"),
    ];

    public IEnumerable<RoleBundleContribution> RoleBundles { get; } = [];
}
