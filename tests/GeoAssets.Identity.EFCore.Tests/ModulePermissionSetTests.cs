using FluentAssertions;
using GeoAssets.Identity.Authorization.Modules;
using Xunit;

namespace GeoAssets.Identity.EFCore.Tests;

public sealed class ModulePermissionSetTests
{
    private static readonly PermissionDefinition Good = new("a:read", "a", "read", "permissions.a.read");
    private static readonly RoleBundleContribution GoodBundle = new(Guid.NewGuid(), "Role", "d", ["a:read"]);

    public static TheoryData<string, ModulePermissionSet, string> Problems => new()
    {
        { "null permission", new() { Permissions = [null!] }, "permission is missing" },
        { "blank code", new() { Permissions = [new(" ", "a", "read", "k")] }, "needs a code" },
        { "blank resource", new() { Permissions = [new("a:read", "", "read", "k")] }, "needs a code" },
        { "blank action", new() { Permissions = [new("a:read", "a", " ", "k")] }, "needs a code" },
        { "blank description key", new() { Permissions = [new("a:read", "a", "read", "")] }, "needs a code" },
        { "duplicate code", new() { Permissions = [Good, Good] }, "'a:read' is defined twice" },
        { "null bundle", new() { RoleBundles = [null!] }, "role bundle is missing" },
        { "empty role id", new() { RoleBundles = [new(Guid.Empty, "Role", "d", [])] }, "role id and a role name" },
        { "blank role name", new() { RoleBundles = [new(Guid.NewGuid(), " ", "d", [])] }, "role id and a role name" },
        { "null permission codes", new() { RoleBundles = [new(Guid.NewGuid(), "Role", "d", null!)] }, "blank permission code" },
        { "null code in bundle", new() { RoleBundles = [new(Guid.NewGuid(), "Role", "d", [null!])] }, "blank permission code" },
        { "blank code in bundle", new() { RoleBundles = [new(Guid.NewGuid(), "Role", "d", ["a:read", " "])] }, "blank permission code" },
    };

    [Theory]
    [MemberData(nameof(Problems))]
    public void FindProblem_InvalidSet_ReportsTheProblem(string scenario, ModulePermissionSet set, string expectedFragment)
    {
        ArgumentNullException.ThrowIfNull(set);
        set.FindProblem().Should().Contain(expectedFragment, scenario);
    }

    [Fact]
    public void FindProblem_ValidSet_ReturnsNull()
    {
        new ModulePermissionSet { Permissions = [Good], RoleBundles = [GoodBundle] }.FindProblem().Should().BeNull();
    }

    [Fact]
    public void FindProblem_EmptySet_ReturnsNull() => new ModulePermissionSet().FindProblem().Should().BeNull();

    [Fact]
    public void Lists_AssignedNull_BecomeEmpty()
    {
        var set = new ModulePermissionSet { Permissions = null!, RoleBundles = null! };

        set.Permissions.Should().BeEmpty();
        set.RoleBundles.Should().BeEmpty();
    }
}
