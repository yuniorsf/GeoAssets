using GeoAssets.Core.Navigation;

namespace GeoAssets.Shared.Navigation;

/// <summary>"Identidad" sub-item linking to the Users admin page (XD01-85).</summary>
public sealed class AdminUsersMenuItem : MenuPageItem
{
    public override string Id => "admin-users";
    public override string LabelKey => "admin.users.title";
    public override string? ParentId => "identity";
    public override int SortOrder => 0;
    public override string RouteHref => "admin/users";
}
