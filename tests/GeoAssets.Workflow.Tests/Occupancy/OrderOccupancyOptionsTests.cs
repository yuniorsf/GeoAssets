using FluentAssertions;
using GeoAssets.Core.Models;
using GeoAssets.NeutralOperator.Application.Occupancy;
using Xunit;

namespace GeoAssets.Workflow.Tests.Occupancy;

/// <summary>What makes a feature a pole: an asset type carrying the pole role, or a configured legacy id (XD01-368).</summary>
public sealed class OrderOccupancyOptionsTests
{
    private static readonly Guid PoleTypeId = Guid.Parse("00000000-0000-0000-0000-000000000004");

    private static AssetType Type(Guid id, params string[] roles) => new() { Id = id, Name = "Type", Roles = roles };

    [Fact]
    public void PoleRole_DefaultsToOccupiableSupport_AndNoIdsAreConfigured()
    {
        var options = new OrderOccupancyOptions();

        options.PoleRole.Should().Be("occupiable-support");
        options.PoleAssetTypeIds.Should().BeEmpty();
    }

    [Theory]
    [InlineData("role-carrying type", true)]
    [InlineData("role-carrying type, id in another case", true)]
    [InlineData("configured id", true)]
    [InlineData("configured id, another case", true)]
    [InlineData("neither", false)]
    [InlineData("type without the role", false)]
    [InlineData("null id", false)]
    public void IsPole_RoleOrConfiguredId(string scenario, bool expected)
    {
        var options = new OrderOccupancyOptions();
        options.PoleAssetTypeIds.Add("legacy-pole");
        var carrier = Type(PoleTypeId, "occupiable-support");
        var plain = Type(Guid.NewGuid());
        IReadOnlyList<AssetType> catalog = [carrier, plain];

        var actual = scenario switch
        {
            "role-carrying type" => options.IsPole(PoleTypeId.ToString(), catalog),
            "role-carrying type, id in another case" => options.IsPole(PoleTypeId.ToString().ToUpperInvariant(), catalog),
            "configured id" => options.IsPole("legacy-pole", []),
            "configured id, another case" => options.IsPole("LEGACY-POLE", []),
            "neither" => options.IsPole("unknown", catalog),
            "type without the role" => options.IsPole(plain.Id.ToString(), catalog),
            _ => options.IsPole(null, catalog),
        };

        actual.Should().Be(expected);
    }

    [Fact]
    public void IsPole_ACustomPoleRole_ReplacesTheDefault()
    {
        var options = new OrderOccupancyOptions { PoleRole = "custom-role" };
        var custom = Type(Guid.NewGuid(), "custom-role");
        var support = Type(Guid.NewGuid(), "occupiable-support");

        options.IsPole(custom.Id.ToString(), [custom, support]).Should().BeTrue();
        options.IsPole(support.Id.ToString(), [custom, support]).Should().BeFalse();
    }

    [Fact]
    public void IsPole_NullAssetTypes_ThrowsArgumentNullException() =>
        ((Action)(() => new OrderOccupancyOptions().IsPole("x", null!))).Should().Throw<ArgumentNullException>();
}
