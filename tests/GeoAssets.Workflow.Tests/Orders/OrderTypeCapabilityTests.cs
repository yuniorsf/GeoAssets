using FluentAssertions;
using GeoAssets.Workflow.Isp.NeutralOperator.SharedUse;
using GeoAssets.Workflow.Occupancy;
using GeoAssets.Workflow.Orders;
using Xunit;

namespace GeoAssets.Workflow.Tests.Orders;

/// <summary><see cref="OrderType.Capabilities"/> and <see cref="OrderType.HasCapability"/> (XD01-361).</summary>
public sealed class OrderTypeCapabilityTests
{
    [Fact]
    public void HasCapability_IsOrdinal()
    {
        var type = new OrderType { Id = "t", DisplayName = "T", Capabilities = [OccupancyCapabilities.Commit] };

        type.HasCapability("occupancy-commit").Should().BeTrue();
        type.HasCapability("OCCUPANCY-COMMIT").Should().BeFalse();
        type.HasCapability("occupancy").Should().BeFalse();
    }

    [Fact]
    public void Default_DeclaresNone()
    {
        var type = new OrderType { Id = "t", DisplayName = "T" };

        type.Capabilities.Should().BeEmpty();
        type.HasCapability(OccupancyCapabilities.Commit).Should().BeFalse();
    }

    [Fact]
    public void SharedUseOrderTypes_DeclareOccupancyCommit()
    {
        SharedUseOrderTypes.CreatePoleWork(["pole"]).HasCapability(OccupancyCapabilities.Commit).Should().BeTrue();
        SharedUseOrderTypes.CreateOccupantNotice(["pole"]).HasCapability(OccupancyCapabilities.Commit).Should().BeTrue();
    }
}
