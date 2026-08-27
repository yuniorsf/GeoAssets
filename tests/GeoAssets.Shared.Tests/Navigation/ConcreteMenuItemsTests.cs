using FluentAssertions;
using GeoAssets.Core.Navigation;
using GeoAssets.Shared.Components.Assets;
using GeoAssets.Shared.Components.Layers;
using GeoAssets.Shared.Components.Providers;
using GeoAssets.Shared.Navigation;
using Xunit;

namespace GeoAssets.Shared.Tests.Navigation;

/// <summary>
/// Verifies the real production <see cref="MenuItemBase"/> subclasses (XD01-85) assemble into
/// the expected tree via the real <see cref="MenuTreeBuilder"/> — unlike
/// <c>MenuTreeBuilderTests</c>/<c>NavMenuTests</c>, which exercise the algorithm with disposable
/// test-double items, this catches an authoring mistake (a typo'd Id/ParentId, a duplicate,
/// a wrong SortOrder) in the actual items NavMenu.razor renders.
/// </summary>
public class ConcreteMenuItemsTests
{
    private static readonly MenuItemBase[] AllItems =
    [
        new OverviewMenuItem(),
        new ManagementSectionItem(),
        new LayersMenuItem(),
        new AssetListMenuItem(),
        new ServiceOrdersMenuItem(),
        new CollectionsMenuItem(),
        new AdministrationSectionItem(),
        new IdentityGroupMenuItem(),
        new AdminUsersMenuItem(),
        new AdminRolesMenuItem(),
        new AdminPermissionsMenuItem(),
    ];

    [Fact]
    public void Build_TopLevelItems_AppearInTodaysVisualOrder()
    {
        var tree = MenuTreeBuilder.Build(AllItems);

        tree.Select(n => n.Item.Id).Should().Equal(
            "overview", "management", "layers", "assets", "service-orders",
            "collections", "administration", "identity");
    }

    [Fact]
    public void Build_IdentityGroup_HasUsersRolesPermissionsChildrenInOrder()
    {
        var tree = MenuTreeBuilder.Build(AllItems);

        var identity = tree.Single(n => n.Item.Id == "identity");

        identity.Children.Select(c => c.Item.Id).Should().Equal(
            "admin-users", "admin-roles", "admin-permissions");
    }

    [Fact]
    public void AllItems_HaveUniqueIds()
    {
        AllItems.Select(item => item.Id).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void IdentityGroupAndAdministrationSection_BothRequireUsersReadPermission()
    {
        // Administration's section label isn't a parent of Identidad (MenuSectionItem is never a
        // parent), so pruning Identidad's subtree alone wouldn't hide this sibling label — both
        // must carry the same RequiredPermission for parity with today's single ShowAdmin check.
        new IdentityGroupMenuItem().RequiredPermission.Should().Be("users:read");
        new AdministrationSectionItem().RequiredPermission.Should().Be("users:read");
    }

    [Fact]
    public void PanelItems_ComponentTypesAreRealBlazorComponents()
    {
        new LayersMenuItem().ComponentType.Should().Be(typeof(LayerManager));
        new AssetListMenuItem().ComponentType.Should().Be(typeof(AssetList));
        new CollectionsMenuItem().ComponentType.Should().Be(typeof(ProviderPoolPanel));
    }

    [Fact]
    public void OverviewMenuItem_MatchesExactlyLikeTodaysNavLinkMatchAll()
    {
        var overview = new OverviewMenuItem();

        overview.RouteHref.Should().BeEmpty();
        overview.Match.Should().Be(MenuLinkMatch.Exact);
    }
}
