using FluentAssertions;
using GeoAssets.Workflow.Orders;
using Xunit;

namespace GeoAssets.Workflow.Tests.Orders;

public class OrderTransitionContextTests
{
    private static OrderTransitionContext Context(OrderType? orderType, string to) =>
        new(new ServiceOrder { Id = "o" }, orderType, ServiceOrderStatus.InProgress, to, DateTimeOffset.UnixEpoch);

    [Theory]
    [InlineData(ServiceOrderStatus.Completed, true)]
    [InlineData(ServiceOrderStatus.Cancelled, true)]
    [InlineData(ServiceOrderStatus.OnHold, false)]
    [InlineData(ServiceOrderStatus.InProgress, false)]
    public void IsTerminal_WithoutAnOrderType_FollowsTheDefaultGraph(string to, bool expected) =>
        Context(null, to).IsTerminal.Should().Be(expected);

    [Fact]
    public void IsTerminal_WithACustomGraph_FollowsThatGraph()
    {
        var type = new OrderType
        {
            Id = "t",
            DisplayName = "t",
            States = [new("open", "Open"), new("shut", "Shut")],
            Transitions = [new("open", "shut", OrderActionType.Approve)],
        };

        Context(type, "shut").IsTerminal.Should().BeTrue();
        Context(type, "open").IsTerminal.Should().BeFalse();
    }
}
