using System.Diagnostics;

namespace GeoAssets.Core.Navigation;

/// <summary>
/// Assembles a flat set of discovered <see cref="MenuItemBase"/> instances (from
/// <see cref="MenuRegistry.All"/>) into a parent/child tree, and answers which
/// <see cref="MenuGroupItem"/> nodes should start expanded for a given route.
/// </summary>
public static class MenuTreeBuilder
{
    /// <summary>
    /// Groups <paramref name="items"/> by <see cref="MenuItemBase.ParentId"/> and sorts siblings
    /// by <see cref="MenuItemBase.SortOrder"/>. An item whose <c>ParentId</c> doesn't match any
    /// other item's <c>Id</c> (a typo, or that parent's assembly wasn't registered) is promoted
    /// to a top-level item with a logged warning, rather than silently dropped or failing startup.
    /// </summary>
    public static IReadOnlyList<MenuNode> Build(IReadOnlyList<MenuItemBase> items)
    {
        var idsInUse = items.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var childrenByParentId = items.ToLookup(item => item.ParentId, StringComparer.Ordinal);

        foreach (var item in items)
        {
            if (item.ParentId is { } parentId && !idsInUse.Contains(parentId))
                Trace.TraceWarning(
                    $"MenuTreeBuilder: item '{item.Id}' declares ParentId '{parentId}', which " +
                    "doesn't match any registered item's Id. Promoting it to a top-level item.");
        }

        var topLevelItems = items.Where(
            item => item.ParentId is not { } parentId || !idsInUse.Contains(parentId));

        return BuildNodes(topLevelItems, childrenByParentId);
    }

    private static IReadOnlyList<MenuNode> BuildNodes(
        IEnumerable<MenuItemBase> items, ILookup<string?, MenuItemBase> childrenByParentId) =>
        items
            .OrderBy(item => item.SortOrder)
            .Select(item => new MenuNode(item, BuildNodes(childrenByParentId[item.Id], childrenByParentId)))
            .ToList();

    /// <summary>
    /// Generalizes <c>NavMenu.ShouldExpandIdentityGroup</c> to work for any
    /// <see cref="MenuGroupItem"/>: a group is expanded if <paramref name="currentRelativePath"/>
    /// matches one of its (possibly nested) descendant <see cref="MenuPageItem"/> routes.
    /// </summary>
    public static IReadOnlySet<string> ComputeExpandedGroupIds(
        IReadOnlyList<MenuNode> tree, string currentRelativePath)
    {
        var trimmedPath = currentRelativePath.TrimStart('/');
        var expandedGroupIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var node in tree)
            ContainsActiveRoute(node, trimmedPath, expandedGroupIds);

        return expandedGroupIds;
    }

    private static bool ContainsActiveRoute(MenuNode node, string trimmedPath, HashSet<string> expandedGroupIds)
    {
        var isActiveHere = node.Item is MenuPageItem page && MatchesRoute(trimmedPath, page);

        var childIsActive = false;
        foreach (var child in node.Children)
            childIsActive |= ContainsActiveRoute(child, trimmedPath, expandedGroupIds);

        var isActive = isActiveHere || childIsActive;
        if (isActive && node.Item is MenuGroupItem group)
            expandedGroupIds.Add(group.Id);

        return isActive;
    }

    private static bool MatchesRoute(string trimmedPath, MenuPageItem page)
    {
        var href = page.RouteHref.TrimStart('/');

        if (page.Match == MenuLinkMatch.Exact)
            return string.Equals(StripQuery(trimmedPath), href, StringComparison.OrdinalIgnoreCase);

        if (!trimmedPath.StartsWith(href, StringComparison.OrdinalIgnoreCase))
            return false;

        // Boundary check so e.g. href "admin/users" doesn't match path "admin/usersextra".
        return trimmedPath.Length == href.Length || trimmedPath[href.Length] is '/' or '?';
    }

    private static string StripQuery(string path)
    {
        var queryIndex = path.IndexOf('?');
        return queryIndex < 0 ? path : path[..queryIndex];
    }
}
