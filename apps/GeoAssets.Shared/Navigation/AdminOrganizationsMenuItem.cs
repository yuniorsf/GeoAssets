using GeoAssets.Core.Navigation;

namespace GeoAssets.Shared.Navigation;

/// <summary>"Identidad" sub-item linking to the Organizations admin page (XD01-128).</summary>
public sealed class AdminOrganizationsMenuItem : MenuPageItem
{
    public override string Id => "admin-organizations";
    public override string LabelKey => "admin.organizations.title";
    public override string? ParentId => "identity";
    public override int SortOrder => 30;
    public override string RouteHref => "admin/organizations";
}
