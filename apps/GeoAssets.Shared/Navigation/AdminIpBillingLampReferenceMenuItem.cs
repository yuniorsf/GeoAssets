using GeoAssets.Core.Navigation;

namespace GeoAssets.Shared.Navigation;

/// <summary>"Identidad" sub-item linking to the IP-billing Lamp Reference admin page (XD01-178).</summary>
public sealed class AdminIpBillingLampReferenceMenuItem : MenuPageItem
{
    public override string Id => "admin-ip-billing-lamp-reference";
    public override string LabelKey => "admin.ipBilling.lampReference.title";
    public override string? ParentId => "identity";
    public override int SortOrder => 50;
    public override string RouteHref => "admin/ip-billing/lamp-reference";
}
