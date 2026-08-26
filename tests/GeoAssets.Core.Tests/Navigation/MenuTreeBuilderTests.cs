using FluentAssertions;
using GeoAssets.Core.Navigation;
using Xunit;

namespace GeoAssets.Core.Tests.Navigation;

public class MenuTreeBuilderTests
{
    // Constructor defaults are only exercised when MenuRegistrationExtensionsTests (in the same
    // test assembly) incidentally discovers and DI-constructs these via reflection — kept
    // distinct from every Id used there so an accidental collision can't cause a spurious
    // duplicate-Id failure in that unrelated test file.
    private sealed class TestGroupItem(
        string id = "test-group-item-default", string? parentId = null, int sortOrder = 0) : MenuGroupItem
    {
        public override string Id => id;
        public override string LabelKey => $"label.{id}";
        public override string? ParentId => parentId;
        public override int SortOrder => sortOrder;
    }

    private sealed class TestPageItem(
        string id = "test-page-item-default", string routeHref = "test-page-item-default",
        string? parentId = null, int sortOrder = 0, MenuLinkMatch match = MenuLinkMatch.Prefix) : MenuPageItem
    {
        public override string Id => id;
        public override string LabelKey => $"label.{id}";
        public override string RouteHref => routeHref;
        public override string? ParentId => parentId;
        public override int SortOrder => sortOrder;
        public override MenuLinkMatch Match => match;
    }

    // ── Build ────────────────────────────────────────────────────────────

    [Fact]
    public void Build_GroupsChildrenUnderTheirParentId()
    {
        var group  = new TestGroupItem(id: "identity");
        var users  = new TestPageItem(id: "users", routeHref: "admin/users", parentId: "identity");
        var roles  = new TestPageItem(id: "roles", routeHref: "admin/roles", parentId: "identity");

        var tree = MenuTreeBuilder.Build([group, users, roles]);

        tree.Should().ContainSingle();
        var groupNode = tree[0];
        groupNode.Item.Id.Should().Be("identity");
        groupNode.Children.Select(c => c.Item.Id).Should().Equal("users", "roles");
    }

    [Fact]
    public void Build_SortsSiblingsBySortOrder()
    {
        var second = new TestPageItem(id: "second", routeHref: "second", sortOrder: 20);
        var first  = new TestPageItem(id: "first", routeHref: "first", sortOrder: 10);

        var tree = MenuTreeBuilder.Build([second, first]);

        tree.Select(n => n.Item.Id).Should().Equal("first", "second");
    }

    [Fact]
    public void Build_OrphanedParentId_PromotesItemToTopLevel()
    {
        // Fails without the fix: an unmatched ParentId would otherwise leave the item nested
        // nowhere (dropped entirely) instead of surfacing it at top level.
        var orphan = new TestPageItem(id: "orphan", routeHref: "orphan", parentId: "does-not-exist");

        var tree = MenuTreeBuilder.Build([orphan]);

        tree.Should().ContainSingle(n => n.Item.Id == "orphan");
    }

    [Fact]
    public void Build_NoItems_ReturnsEmptyTree() =>
        MenuTreeBuilder.Build([]).Should().BeEmpty();

    // ── ComputeExpandedGroupIds ─────────────────────────────────────────
    // Ported from NavMenuTests.ShouldExpandIdentityGroup_MatchesOnlyAdminSubRoutes — XD01-81
    // generalizes NavMenu.ShouldExpandIdentityGroup into this method.

    private static IReadOnlyList<MenuNode> BuildIdentityLikeTree()
    {
        var group       = new TestGroupItem(id: "identity");
        var users       = new TestPageItem(id: "users", routeHref: "admin/users", parentId: "identity");
        var roles       = new TestPageItem(id: "roles", routeHref: "admin/roles", parentId: "identity");
        var permissions = new TestPageItem(id: "permissions", routeHref: "admin/permissions", parentId: "identity");
        return MenuTreeBuilder.Build([group, users, roles, permissions]);
    }

    [Theory]
    [InlineData("admin/users", true)]
    [InlineData("admin/roles", true)]
    [InlineData("admin/permissions", true)]
    [InlineData("admin/users?tab=roles", true)]
    [InlineData("/admin/users", true)]
    [InlineData("", false)]
    [InlineData("/", false)]
    [InlineData("service-orders", false)]
    [InlineData("administration", false)]
    public void ComputeExpandedGroupIds_MatchesOnlyDescendantSubRoutes(string relativePath, bool expected)
    {
        var tree = BuildIdentityLikeTree();

        var expandedIds = MenuTreeBuilder.ComputeExpandedGroupIds(tree, relativePath);

        expandedIds.Contains("identity").Should().Be(expected);
    }

    [Fact]
    public void ComputeExpandedGroupIds_ExactMatch_DoesNotMatchLongerPath()
    {
        var group = new TestGroupItem(id: "reports");
        var page = new TestPageItem(
            id: "summary", routeHref: "reports/summary", parentId: "reports", match: MenuLinkMatch.Exact);
        var tree = MenuTreeBuilder.Build([group, page]);

        MenuTreeBuilder.ComputeExpandedGroupIds(tree, "reports/summary/details")
            .Contains("reports").Should().BeFalse();
    }

    [Fact]
    public void ComputeExpandedGroupIds_NestedGroups_ExpandsEveryAncestor()
    {
        var outer = new TestGroupItem(id: "outer");
        var inner = new TestGroupItem(id: "inner", parentId: "outer");
        var leaf  = new TestPageItem(id: "leaf", routeHref: "outer/inner/leaf", parentId: "inner");
        var tree = MenuTreeBuilder.Build([outer, inner, leaf]);

        var expandedIds = MenuTreeBuilder.ComputeExpandedGroupIds(tree, "outer/inner/leaf");

        expandedIds.Should().Contain(["outer", "inner"]);
    }
}
