using FluentAssertions;
using GeoAssets.Core.Navigation;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Services;
using GeoAssets.Shared.Components.Layout;
using Xunit;

namespace GeoAssets.Shared.Tests.Components.Layout;

public class NavMenuTests
{
    // ShouldExpandIdentityGroup was removed with the NavMenu.razor cutover (XD01-85) — its
    // route-matching logic was generalized into MenuTreeBuilder.ComputeExpandedGroupIds and is
    // now tested there (MenuTreeBuilderTests, GeoAssets.Core.Tests). This file now covers
    // NavMenu's other piece of testable logic: permission-based tree filtering.

    private sealed class StubAuthorizationService(
        Func<string, bool>? hasPermission = null, Exception? throwOn = null) : IGeoAuthorizationService
    {
        public Task<bool> HasPermissionAsync(string permissionCode, CancellationToken ct = default)
        {
            if (throwOn is not null) throw throwOn;
            return Task.FromResult(hasPermission?.Invoke(permissionCode) ?? false);
        }

        public Task<bool> IsInRoleAsync(string roleName, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<bool> HasClaimAsync(string claimType, string? claimValue = null, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<bool> EvaluatePolicyAsync(string policyName, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<bool> EvaluatePolicyAsync(AppPolicy policy, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<AuthorizationContext> GetAuthorizationContextAsync(CancellationToken ct = default) =>
            throw new NotImplementedException();
    }

    private sealed class TestGroupItem(
        string id, string? parentId = null, string? requiredPermission = null) : MenuGroupItem
    {
        public override string Id => id;
        public override string LabelKey => $"label.{id}";
        public override string? ParentId => parentId;
        public override string? RequiredPermission => requiredPermission;
    }

    private sealed class TestPageItem(
        string id, string? parentId = null, string? requiredPermission = null) : MenuPageItem
    {
        public override string Id => id;
        public override string LabelKey => $"label.{id}";
        public override string RouteHref => id;
        public override string? ParentId => parentId;
        public override string? RequiredPermission => requiredPermission;
    }

    [Fact]
    public async Task FilterByPermissionAsync_NoRequiredPermission_KeepsItem()
    {
        var tree = MenuTreeBuilder.Build([new TestPageItem("overview")]);

        var filtered = await NavMenu.FilterByPermissionAsync(tree, authService: null);

        filtered.Should().ContainSingle(n => n.Item.Id == "overview");
    }

    [Fact]
    public async Task FilterByPermissionAsync_PermissionGranted_KeepsItem()
    {
        var tree = MenuTreeBuilder.Build([new TestPageItem("admin", requiredPermission: "users:read")]);
        var authService = new StubAuthorizationService(hasPermission: code => code == "users:read");

        var filtered = await NavMenu.FilterByPermissionAsync(tree, authService);

        filtered.Should().ContainSingle(n => n.Item.Id == "admin");
    }

    [Fact]
    public async Task FilterByPermissionAsync_PermissionDenied_DropsItemAndItsSubtree()
    {
        // Fails without the fix: without pruning, Identidad (and its children) would render for
        // every user regardless of permission — a real UX/security regression vs. today's
        // HasPermissionAsync("users:read")-gated ShowAdmin.
        var group = new TestGroupItem("identity", requiredPermission: "users:read");
        var child = new TestPageItem("admin-users", parentId: "identity");
        var tree  = MenuTreeBuilder.Build([group, child]);
        var authService = new StubAuthorizationService(hasPermission: _ => false);

        var filtered = await NavMenu.FilterByPermissionAsync(tree, authService);

        filtered.Should().BeEmpty();
    }

    [Fact]
    public async Task FilterByPermissionAsync_NoAuthorizationServiceRegistered_DropsGatedItemInsteadOfThrowing()
    {
        // Fails without the fix: MAUI has no IGeoAuthorizationService registration today — a null
        // service here must degrade to "hide the item", not propagate a NullReferenceException
        // and take down the whole nav.
        var tree = MenuTreeBuilder.Build([new TestPageItem("admin", requiredPermission: "users:read")]);

        var filtered = await NavMenu.FilterByPermissionAsync(tree, authService: null);

        filtered.Should().BeEmpty();
    }

    [Fact]
    public async Task FilterByPermissionAsync_AuthorizationServiceThrows_DropsGatedItemInsteadOfThrowing()
    {
        // A backend outage checking the permission must not take down the whole nav — same
        // tradeoff Index.razor's own try/catch around HasPermissionAsync("users:read") made.
        var tree = MenuTreeBuilder.Build([new TestPageItem("admin", requiredPermission: "users:read")]);
        var authService = new StubAuthorizationService(throwOn: new InvalidOperationException("backend down"));

        var filtered = await NavMenu.FilterByPermissionAsync(tree, authService);

        filtered.Should().BeEmpty();
    }

    [Fact]
    public async Task FilterByPermissionAsync_UngatedSiblingOfDeniedItem_IsUnaffected()
    {
        var overview = new TestPageItem("overview");
        var admin    = new TestGroupItem("identity", requiredPermission: "users:read");
        var tree     = MenuTreeBuilder.Build([overview, admin]);
        var authService = new StubAuthorizationService(hasPermission: _ => false);

        var filtered = await NavMenu.FilterByPermissionAsync(tree, authService);

        filtered.Should().ContainSingle(n => n.Item.Id == "overview");
    }
}
