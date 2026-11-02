using GeoAssets.Core.Navigation;

namespace GeoAssets.Shared.Navigation;

/// <summary>"Identidad" sub-item linking to the IP-billing Levantamentos admin page (XD01-181).</summary>
public sealed class AdminIpBillingLevantamentosMenuItem : MenuPageItem
{
    public override string Id => "admin-ip-billing-levantamentos";
    public override string LabelKey => "admin.ipBilling.levantamentos.title";
    public override string? ParentId => "identity";
    public override int SortOrder => 45;
    public override string RouteHref => "admin/ip-billing/levantamentos";
}
