using FluentAssertions;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Persistence;
using GeoAssets.Workflow.Persistence.Entities;
using Xunit;

namespace GeoAssets.Workflow.EFCore.Tests;

/// <summary>The stored form of <see cref="OrderType.Capabilities"/> (XD01-361).</summary>
public sealed class OrderTypeMapperCapabilityTests
{
    [Fact]
    public void ToJson_SortsDedupesAndDropsBlanks()
    {
        OrderTypeMapper.CapabilitiesToJson(["zeta", "alpha", "alpha", " ", "", "Alpha"])
            .Should().Be("""["Alpha","alpha","zeta"]""");
    }

    [Fact]
    public void ToJson_NullWhenNoneRemain()
    {
        OrderTypeMapper.CapabilitiesToJson([]).Should().BeNull();
        OrderTypeMapper.CapabilitiesToJson([" ", ""]).Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FromJson_NullOrBlank_IsEmpty(string? json)
    {
        OrderTypeMapper.CapabilitiesFromJson(json).Should().BeEmpty();
    }

    [Fact]
    public void RoundTrip_ThroughTheRecord_KeepsTheCapabilities()
    {
        var type = new OrderType { Id = "t", DisplayName = "T", Capabilities = ["occupancy-commit", "other"] };

        var record = OrderTypeMapper.ToRecord(type);

        record.CapabilitiesJson.Should().Be("""["occupancy-commit","other"]""");
        OrderTypeMapper.ToDomain(record).Capabilities.Should().Equal("occupancy-commit", "other");
        OrderTypeMapper.ToDomain(new OrderTypeRecord { Id = "t", DisplayName = "T" }).Capabilities.Should().BeEmpty();
    }
}
