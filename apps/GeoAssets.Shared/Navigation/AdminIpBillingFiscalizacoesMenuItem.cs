using GeoAssets.Core.Navigation;

namespace GeoAssets.Shared.Navigation;

/// <summary>"Identidad" sub-item linking to the IP-billing Fiscalizações admin page (XD01-184).</summary>
public sealed class AdminIpBillingFiscalizacoesMenuItem : MenuPageItem
{
    public override string Id => "admin-ip-billing-fiscalizacoes";
    public override string LabelKey => "admin.ipBilling.fiscalizacoes.title";
    public override string? ParentId => "identity";
    public override int SortOrder => 46;
    public override string RouteHref => "admin/ip-billing/fiscalizacoes";
}
