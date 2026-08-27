using GeoAssets.Core.Navigation;

namespace GeoAssets.Shared.Navigation;

/// <summary>"Identidad" sub-item linking to the Roles admin page (XD01-85).</summary>
public sealed class AdminRolesMenuItem : MenuPageItem
{
    public override string Id => "admin-roles";
    public override string LabelKey => "admin.roles.title";
    public override string? ParentId => "identity";
    public override int SortOrder => 10;
    public override string RouteHref => "admin/roles";
}
