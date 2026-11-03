using GeoAssets.Core.Navigation;

namespace GeoAssets.Shared.Navigation;

/// <summary>"Identidad" sub-item linking to the IP-billing Ofícios admin page (XD01-187).</summary>
public sealed class AdminIpBillingOficiosMenuItem : MenuPageItem
{
    public override string Id => "admin-ip-billing-oficios";
    public override string LabelKey => "admin.ipBilling.oficios.title";
    public override string? ParentId => "identity";
    public override int SortOrder => 47;
    public override string RouteHref => "admin/ip-billing/oficios";
}
