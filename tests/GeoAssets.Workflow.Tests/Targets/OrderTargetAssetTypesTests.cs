using FluentAssertions;
using GeoAssets.Core.Models;
using GeoAssets.Workflow.Targets;
using Xunit;

namespace GeoAssets.Workflow.Tests.Targets;

/// <summary>The asset types a target policy resolves to: its ids plus the types carrying its roles (XD01-368).</summary>
public sealed class OrderTargetAssetTypesTests
{
    private const string Support = "occupiable-support";

    private static AssetType Type(params string[] roles) => new() { Id = Guid.NewGuid(), Name = "Type", Roles = roles };

    [Fact]
    public void Resolve_IdsOnly_ReturnsTheIds_WhateverTheCatalogHolds()
    {
        var policy = new OrderTargetPolicy { TargetAssetTypeIds = ["pole", "manhole"] };

        OrderTargetAssetTypes.Resolve(policy, [Type(Support)]).Should().Equal("pole", "manhole");
    }

    [Fact]
    public void Resolve_RolesOnly_ReturnsTheIdsOfTheTypesCarryingAnyTargetedRole()
    {
        var a = Type(Support);
        var b = Type("other", "ducted-run");
        var none = Type("other");
        var policy = new OrderTargetPolicy { TargetAssetTypeRoles = [Support, "ducted-run"] };

        OrderTargetAssetTypes.Resolve(policy, [a, none, b]).Should().Equal(a.Id.ToString(), b.Id.ToString());
    }

    [Fact]
    public void Resolve_IdsAndRoles_AreAUnionWithTheIdsFirst_DistinctIgnoringCase()
    {
        var carrier = Type(Support);
        var upper = carrier.Id.ToString().ToUpperInvariant();
        var policy = new OrderTargetPolicy
        {
            TargetAssetTypeIds = ["pole", "POLE", upper],
            TargetAssetTypeRoles = [Support],
        };
        var other = Type(Support);

        OrderTargetAssetTypes.Resolve(policy, [other, carrier]).Should().Equal("pole", upper, other.Id.ToString());
    }

    [Fact]
    public void Resolve_ATypeCarryingTwoTargetedRoles_AppearsOnce()
    {
        var both = Type(Support, "ducted-run");
        var policy = new OrderTargetPolicy { TargetAssetTypeRoles = [Support, "ducted-run"] };

        OrderTargetAssetTypes.Resolve(policy, [both]).Should().Equal(both.Id.ToString());
    }

    [Fact]
    public void Resolve_ATypeWithoutTheRole_IsExcluded()
    {
        var policy = new OrderTargetPolicy { TargetAssetTypeIds = ["pole"], TargetAssetTypeRoles = [Support] };

        OrderTargetAssetTypes.Resolve(policy, [Type(), Type("other")]).Should().Equal("pole");
    }

    [Fact]
    public void Resolve_RolesAreMatchedOrdinally_SoADifferentCaseDoesNotMatch()
    {
        var policy = new OrderTargetPolicy { TargetAssetTypeRoles = [Support] };

        OrderTargetAssetTypes.Resolve(policy, [Type("Occupiable-Support")]).Should().BeEmpty();
    }

    [Fact]
    public void Resolve_NoRoles_ReturnsOnlyTheIds()
    {
        var policy = new OrderTargetPolicy { TargetAssetTypeIds = ["pole"] };

        OrderTargetAssetTypes.Resolve(policy, [Type(Support)]).Should().Equal("pole");
    }

    [Fact]
    public void Resolve_NullArguments_ThrowArgumentNullException()
    {
        ((Action)(() => OrderTargetAssetTypes.Resolve(null!, []))).Should().Throw<ArgumentNullException>();
        ((Action)(() => OrderTargetAssetTypes.Resolve(new OrderTargetPolicy(), null!))).Should().Throw<ArgumentNullException>();
    }
}
