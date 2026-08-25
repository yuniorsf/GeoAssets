using FluentAssertions;
using GeoAssets.Shared.Components.Layout;
using Xunit;

namespace GeoAssets.Shared.Tests.Components.Layout;

public class NavMenuTests
{
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
    public void ShouldExpandIdentityGroup_MatchesOnlyAdminSubRoutes(string relativePath, bool expected) =>
        NavMenu.ShouldExpandIdentityGroup(relativePath).Should().Be(expected);
}
