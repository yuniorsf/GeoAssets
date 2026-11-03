using GeoAssets.Core.Navigation;

namespace GeoAssets.Shared.Navigation;

/// <summary>Links to the tenant ISP self-service portal (XD01-269).</summary>
public sealed class IspTenantPortalMenuItem : MenuPageItem
{
    public override string Id => "isp-tenant-portal";
    public override string LabelKey => "isp.tenantPortal.title";
    public override string? ParentId => "identity";
    public override int SortOrder => 49;
    public override string RouteHref => "isp/tenant-portal";
}
