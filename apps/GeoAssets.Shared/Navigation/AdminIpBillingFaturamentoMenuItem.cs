using GeoAssets.Core.Navigation;

namespace GeoAssets.Shared.Navigation;

/// <summary>"Identidad" sub-item linking to the IP-billing Faturamento ledger admin page (XD01-190).</summary>
public sealed class AdminIpBillingFaturamentoMenuItem : MenuPageItem
{
    public override string Id => "admin-ip-billing-faturamento";
    public override string LabelKey => "admin.ipBilling.faturamento.title";
    public override string? ParentId => "identity";
    public override int SortOrder => 48;
    public override string RouteHref => "admin/ip-billing/faturamento";
}
