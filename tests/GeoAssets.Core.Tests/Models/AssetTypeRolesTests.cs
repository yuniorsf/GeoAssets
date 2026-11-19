using FluentAssertions;
using GeoAssets.Core.Models;
using Xunit;

namespace GeoAssets.Core.Tests.Models;

/// <summary>
/// <see cref="AssetTypeRoles"/>'s rules (XD01-367): count, length, lower-kebab pattern with an
/// optional namespace, duplicates, and the ordinal <see cref="AssetTypeRoles.HasRole"/> lookup.
/// </summary>
public class AssetTypeRolesTests
{
    private static string[] Distinct(int count) => [.. Enumerable.Range(0, count).Select(i => $"role-{i}")];

    // ── Valid ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("support")]
    [InlineData("occupiable-support")]
    [InlineData("isp:pole")]
    [InlineData("ip-billing:lamp-group-24h")]
    [InlineData("24h")]
    [InlineData("a1-b2:c3-d4")]
    public void FindDefect_ValidRole_IsNull(string role)
    {
        AssetTypeRoles.FindDefect([role], out var offending).Should().BeNull();
        offending.Should().BeNull();
        AssetTypeRoles.IsWellFormed(role).Should().BeTrue();

        var act = () => AssetTypeRoles.EnsureValid([role]);
        act.Should().NotThrow();
    }

    [Fact]
    public void FindDefect_NullOrEmptyRoles_AreValid()
    {
        AssetTypeRoles.FindDefect(null, out var offending).Should().BeNull();
        offending.Should().BeNull();
        AssetTypeRoles.FindDefect([]).Should().BeNull();
        AssetTypeRoles.EnsureValid(null);
        AssetTypeRoles.EnsureValid([]);
    }

    [Fact]
    public void FindDefect_ExactlyMaxCountRoles_IsValid()
    {
        AssetTypeRoles.MaxCount.Should().Be(16);

        AssetTypeRoles.FindDefect(Distinct(AssetTypeRoles.MaxCount)).Should().BeNull();
    }

    [Fact]
    public void FindDefect_ExactlyMaxLengthRole_IsValid()
    {
        AssetTypeRoles.MaxLength.Should().Be(64);
        var role = new string('a', AssetTypeRoles.MaxLength);

        AssetTypeRoles.FindDefect([role]).Should().BeNull();
        AssetTypeRoles.IsWellFormed(role).Should().BeTrue();
    }

    // ── Defects ──────────────────────────────────────────────────────────────

    [Fact]
    public void FindDefect_MoreThanMaxCountRoles_IsTooManyRolesWithNoOffendingRole()
    {
        var roles = Distinct(AssetTypeRoles.MaxCount + 1);

        AssetTypeRoles.FindDefect(roles, out var offending).Should().Be(AssetTypeRoleDefect.TooManyRoles);
        offending.Should().BeNull();
    }

    [Fact]
    public void FindDefect_RoleLongerThanMaxLength_IsRoleTooLongNamingTheRole()
    {
        var role = new string('a', AssetTypeRoles.MaxLength + 1);

        AssetTypeRoles.FindDefect(["ok", role], out var offending).Should().Be(AssetTypeRoleDefect.RoleTooLong);
        offending.Should().Be(role);
        AssetTypeRoles.IsWellFormed(role).Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Pole")]
    [InlineData("ISP:POLE")]
    [InlineData("has space")]
    [InlineData("a--b")]
    [InlineData("-a")]
    [InlineData("a-")]
    [InlineData("a:")]
    [InlineData(":a")]
    [InlineData("a:b:c")]
    [InlineData("a_b")]
    [InlineData("pole\n")]
    public void FindDefect_MalformedRole_IsMalformedRoleNamingTheRole(string role)
    {
        AssetTypeRoles.FindDefect(["fine", role], out var offending).Should().Be(AssetTypeRoleDefect.MalformedRole);
        offending.Should().Be(role);
        AssetTypeRoles.IsWellFormed(role).Should().BeFalse();
    }

    [Fact]
    public void IsWellFormed_Null_IsFalse() => AssetTypeRoles.IsWellFormed(null).Should().BeFalse();

    [Fact]
    public void FindDefect_RepeatedRole_IsDuplicateRoleNamingTheRole()
    {
        AssetTypeRoles.FindDefect(["a", "b", "a"], out var offending).Should().Be(AssetTypeRoleDefect.DuplicateRole);
        offending.Should().Be("a");
    }

    [Fact]
    public void FindDefect_NamespacedAndBareRolesWithTheSameTail_AreNotDuplicates() =>
        AssetTypeRoles.FindDefect(["pole", "isp:pole"]).Should().BeNull();

    [Fact]
    public void FindDefect_CountIsCheckedBeforeEachRole()
    {
        var roles = new[] { "BAD" }.Concat(Distinct(AssetTypeRoles.MaxCount)).ToArray();

        AssetTypeRoles.FindDefect(roles).Should().Be(AssetTypeRoleDefect.TooManyRoles);
    }

    [Fact]
    public void FindDefect_LengthIsCheckedBeforePattern()
    {
        var role = new string('A', AssetTypeRoles.MaxLength + 1);

        AssetTypeRoles.FindDefect([role]).Should().Be(AssetTypeRoleDefect.RoleTooLong);
    }

    [Fact]
    public void FindDefect_ReportsTheFirstBrokenRoleInOrder()
    {
        AssetTypeRoles.FindDefect(["a", "BAD", "a"], out var offending).Should().Be(AssetTypeRoleDefect.MalformedRole);
        offending.Should().Be("BAD");
    }

    // ── EnsureValid / exception ──────────────────────────────────────────────

    [Fact]
    public void EnsureValid_InvalidRole_ThrowsWithDefectAndRole()
    {
        var act = () => AssetTypeRoles.EnsureValid(["good", "Bad Role"]);

        var thrown = act.Should().Throw<InvalidAssetTypeRolesException>().Which;
        thrown.Defect.Should().Be(AssetTypeRoleDefect.MalformedRole);
        thrown.Role.Should().Be("Bad Role");
        thrown.Message.Should().Contain("Bad Role").And.Contain(nameof(AssetTypeRoleDefect.MalformedRole));
    }

    [Fact]
    public void EnsureValid_TooManyRoles_ThrowsWithNoRole()
    {
        var act = () => AssetTypeRoles.EnsureValid(Distinct(AssetTypeRoles.MaxCount + 1));

        var thrown = act.Should().Throw<InvalidAssetTypeRolesException>().Which;
        thrown.Defect.Should().Be(AssetTypeRoleDefect.TooManyRoles);
        thrown.Role.Should().BeNull();
        thrown.Message.Should().Contain(nameof(AssetTypeRoleDefect.TooManyRoles));
    }

    [Fact]
    public void Exception_StandardConstructors_Work()
    {
        var inner = new InvalidOperationException("inner");

        new InvalidAssetTypeRolesException().Role.Should().BeNull();
        new InvalidAssetTypeRolesException().Defect.Should().Be(default(AssetTypeRoleDefect));
        new InvalidAssetTypeRolesException("m").Message.Should().Be("m");
        new InvalidAssetTypeRolesException("m", inner).InnerException.Should().BeSameAs(inner);
    }

    // ── HasRole ──────────────────────────────────────────────────────────────

    [Fact]
    public void HasRole_CarriedRole_IsTrue()
    {
        var assetType = new AssetType { Roles = ["occupiable-support", "isp:pole"] };

        AssetTypeRoles.HasRole(assetType, "isp:pole").Should().BeTrue();
        AssetTypeRoles.HasRole(assetType, "occupiable-support").Should().BeTrue();
    }

    [Fact]
    public void HasRole_IsCaseSensitiveAndOrdinal()
    {
        var assetType = new AssetType { Roles = ["isp:pole"] };

        AssetTypeRoles.HasRole(assetType, "ISP:POLE").Should().BeFalse();
        AssetTypeRoles.HasRole(assetType, "isp").Should().BeFalse();
        AssetTypeRoles.HasRole(assetType, "pole").Should().BeFalse();
    }

    [Fact]
    public void HasRole_TypeWithoutRoles_IsFalse() =>
        AssetTypeRoles.HasRole(AssetType.Point, "isp:pole").Should().BeFalse();

    [Fact]
    public void HasRole_NullArguments_Throw()
    {
        var nullType = () => AssetTypeRoles.HasRole(null!, "a");
        var nullRole = () => AssetTypeRoles.HasRole(new AssetType(), null!);

        nullType.Should().Throw<ArgumentNullException>();
        nullRole.Should().Throw<ArgumentNullException>();
    }
}
