using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using Xunit;

namespace GeoAssets.Core.Tests.Interfaces;

/// <summary>
/// <see cref="IAssetProvider.GetAssetTypesWithRole"/>'s default implementation (XD01-367), via
/// <see cref="TestAssetProvider"/>, which does not override it. Callers are typed as
/// <see cref="IAssetProvider"/> because default interface members are only reachable that way.
/// </summary>
public class AssetProviderRoleLookupTests
{
    private static AssetType Typed(string name, params string[] roles) => new() { Name = name, Roles = roles };

    private static (IAssetProvider Sut, AssetType First, AssetType Second, AssetType Other) Seeded()
    {
        var provider = new TestAssetProvider();
        var first = Typed("First", "occupiable-support", "isp:pole");
        var second = Typed("Second", "occupiable-support");
        var other = Typed("Other", "ip-billing:pole");
        provider.AddAssetType(first);
        provider.AddAssetType(other);
        provider.AddAssetType(second);
        return (provider, first, second, other);
    }

    [Fact]
    public void GetAssetTypesWithRole_MatchingTypes_ArriveInGetAssetTypesOrder()
    {
        var (sut, first, second, _) = Seeded();

        sut.GetAssetTypesWithRole("occupiable-support").Should().Equal(first, second);
    }

    [Fact]
    public void GetAssetTypesWithRole_MatchIsOrdinalAndCaseSensitive()
    {
        var (sut, _, _, _) = Seeded();

        sut.GetAssetTypesWithRole("OCCUPIABLE-SUPPORT").Should().BeEmpty();
        sut.GetAssetTypesWithRole("pole").Should().BeEmpty();
        sut.GetAssetTypesWithRole("isp").Should().BeEmpty();
    }

    [Fact]
    public void GetAssetTypesWithRole_NoTypeCarriesTheRole_IsEmpty()
    {
        var (sut, _, _, _) = Seeded();

        sut.GetAssetTypesWithRole("unknown-role").Should().BeEmpty();
    }

    [Fact]
    public void GetAssetTypesWithRole_TypeWithoutRoles_NeverMatches()
    {
        var (sut, _, _, _) = Seeded();

        sut.GetAssetTypesWithRole("isp:pole").Should().NotContain(AssetType.Point);
        sut.GetAssetTypesWithRole("isp:pole").Should().ContainSingle();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void GetAssetTypesWithRole_NullOrBlankRole_Throws(string? role)
    {
        var (sut, _, _, _) = Seeded();

        var act = () => sut.GetAssetTypesWithRole(role!);

        act.Should().Throw<ArgumentException>();
    }
}
