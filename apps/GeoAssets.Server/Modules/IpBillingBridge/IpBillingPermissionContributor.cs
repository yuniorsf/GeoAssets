using GeoAssets.Identity.Authorization.Modules;

namespace GeoAssets.Server.Modules.IpBillingBridge;

/// <summary>
/// The IP-billing vertical's permissions (XD01-377), contributed to the kernel's identity seeding instead of being
/// hard-coded in it — XD01-176's reference-data and Levantamento/Fiscalização/Ofício permissions. No role bundles: the
/// role wiring was left to the phase that first needs these permissions, so none is granted here either.
/// </summary>
public sealed class IpBillingPermissionContributor : IModulePermissionContributor
{
    public string VerticalId => IpBillingBridgeModule.ModuleId;

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
