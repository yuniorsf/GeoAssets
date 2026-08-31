using GeoAssets.Core.Navigation;
using GeoAssets.Identity.Authorization.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GeoAssets.Shared.Components.Layout;

public partial class NavMenu
{
    // Matches AssetListMenuItem.Id — Index.razor's old SidebarPanel.AssetList default (that
    // panel opens on first load, before this ticket).
    private const string DefaultOpenPanelId = "assets";

    private IReadOnlyList<MenuNode> _tree = [];
    private HashSet<string> _expandedGroupIds = new(StringComparer.Ordinal);
    private string? _openPanelId = DefaultOpenPanelId;

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();

        // IGeoAuthorizationService isn't registered on every host (e.g. GeoAssets.MAUI today) —
        // resolved optionally via IServiceProvider rather than @inject, since @inject would throw
        // at component-initialization time (before this method even runs) on a host where it's
        // unregistered. FilterByPermissionAsync treats a null service the same as a failed check:
        // hide the item, don't crash.
        var authService = ServiceProvider.GetService<IGeoAuthorizationService>();

        var fullTree = MenuTreeBuilder.Build(Registry.All);
        _tree = await FilterByPermissionAsync(fullTree, authService);
        _expandedGroupIds = new HashSet<string>(
            MenuTreeBuilder.ComputeExpandedGroupIds(_tree, Nav.ToBaseRelativePath(Nav.Uri)),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// Recursively drops any node whose <see cref="MenuItemBase.RequiredPermission"/> check fails
    /// — and its whole subtree with it, matching today's single check gating Identidad and all 3
    /// of its children together. A null <paramref name="authService"/> or a thrown exception from
    /// <see cref="IGeoAuthorizationService.HasPermissionAsync"/> both degrade to "hide the item",
    /// not a crash — showing the admin nav icon (or any permission-gated item) is optional, the
    /// same tradeoff Index.razor's own HasPermissionAsync("users:read") call already made.
    /// </summary>
    public static async Task<IReadOnlyList<MenuNode>> FilterByPermissionAsync(
        IReadOnlyList<MenuNode> nodes, IGeoAuthorizationService? authService)
    {
        var result = new List<MenuNode>(nodes.Count);

        foreach (var node in nodes)
        {
            if (node.Item.RequiredPermission is { } permission)
            {
                var allowed = false;
                if (authService is not null)
                {
                    try
                    {
                        allowed = await authService.HasPermissionAsync(permission);
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine(
                            $"[NavMenu] Failed to evaluate permission '{permission}' for item '{node.Item.Id}': {ex.Message}");
                    }
                }

                if (!allowed) continue;
            }

            var filteredChildren = await FilterByPermissionAsync(node.Children, authService);
            result.Add(node with { Children = filteredChildren });
        }

        return result;
    }

    private void ToggleGroup(string groupId)
    {
        if (!_expandedGroupIds.Remove(groupId))
            _expandedGroupIds.Add(groupId);
    }

    private void TogglePanel(string panelId) =>
        _openPanelId = _openPanelId == panelId ? null : panelId;
}
