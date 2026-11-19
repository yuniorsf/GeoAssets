using GeoAssets.Core.Navigation;

namespace GeoAssets.IpBilling.UI.Navigation;

/// <summary>"Identidad" sub-item linking to the IP-billing Distribuidoras admin page (XD01-301).</summary>
public sealed class AdminIpBillingDistribuidorasMenuItem : MenuPageItem
{
    public override string Id => "admin-ip-billing-distribuidoras";
    public override string LabelKey => "admin.ipBilling.distribuidoras.title";
    public override string? ParentId => "identity";
    public override int SortOrder => 41;
    public override string RouteHref => "admin/ip-billing/distribuidoras";
}
