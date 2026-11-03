using GeoAssets.Core.Navigation;

namespace GeoAssets.Shared.Navigation;

/// <summary>"Identidad" sub-item linking to the regulatory compliance reporting dashboard (XD01-233).</summary>
public sealed class AdminRegulatoryComplianceDashboardMenuItem : MenuPageItem
{
    public override string Id => "admin-reporting-regulatory-compliance";
    public override string LabelKey => "admin.reporting.regulatoryDashboard.title";
    public override string? ParentId => "identity";
    public override int SortOrder => 55;
    public override string RouteHref => "admin/reporting/regulatory-compliance";
}
