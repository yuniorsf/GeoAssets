using GeoAssets.Core.Navigation;

namespace GeoAssets.Shared.Navigation;

/// <summary>"Identidad" sub-item linking to the IP-billing Municípios admin page (XD01-178).</summary>
public sealed class AdminIpBillingMunicipiosMenuItem : MenuPageItem
{
    public override string Id => "admin-ip-billing-municipios";
    public override string LabelKey => "admin.ipBilling.municipios.title";
    public override string? ParentId => "identity";
    public override int SortOrder => 40;
    public override string RouteHref => "admin/ip-billing/municipios";
}
