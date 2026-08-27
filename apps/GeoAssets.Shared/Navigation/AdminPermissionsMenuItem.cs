using GeoAssets.Core.Navigation;

namespace GeoAssets.Shared.Navigation;

/// <summary>"Identidad" sub-item linking to the Permissions admin page (XD01-85).</summary>
public sealed class AdminPermissionsMenuItem : MenuPageItem
{
    public override string Id => "admin-permissions";
    public override string LabelKey => "admin.permissions.title";
    public override string? ParentId => "identity";
    public override int SortOrder => 20;
    public override string RouteHref => "admin/permissions";
}
