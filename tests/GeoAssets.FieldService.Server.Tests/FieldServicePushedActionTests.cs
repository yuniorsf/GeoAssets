using FluentAssertions;
using GeoAssets.FieldService.FieldWork;
using GeoAssets.FieldService.Server;
using GeoAssets.Workflow.Orders;
using Xunit;

namespace GeoAssets.FieldService.Server.Tests;

/// <summary>
/// <see cref="FieldServiceCrossOrganizationAccess.PushedAction"/> (XD01-328): the action a mobile push of another
/// organization's order is decided as — the order type's own tag for the move, the default graph's terminal moves, and
/// <see cref="OrderAction.Execute"/> for an execution update.
/// </summary>
public sealed class FieldServicePushedActionTests
{
    private static readonly OrderType FieldWork = FieldWorkOrderTypes.Create();

    [Theory]
    [InlineData(FieldWorkStatus.InProgress, FieldWorkStatus.Completed, "Complete")]
    [InlineData(FieldWorkStatus.Dispatched, FieldWorkStatus.EnRoute, "Accept")]
    [InlineData(FieldWorkStatus.OnSite, FieldWorkStatus.InProgress, "Execute")]
    [InlineData(FieldWorkStatus.Pending, FieldWorkStatus.Cancelled, "Cancel")]
    [InlineData(FieldWorkStatus.Completed, FieldWorkStatus.Inspected, "Approve")]
    public void AMoveTheTypesGraphTags_IsTheTaggedAction(string from, string to, string expected) =>
        FieldServiceCrossOrganizationAccess.PushedAction(FieldWork, from, to).Should().Be(new OrderAction(expected));

    [Theory]
    [InlineData(FieldWorkStatus.EnRoute, FieldWorkStatus.OnSite)]
    [InlineData(FieldWorkStatus.InProgress, FieldWorkStatus.Suspended)]
    [InlineData(FieldWorkStatus.Draft, FieldWorkStatus.Completed)]
    public void AnUntaggedMoveInTheGraph_OrOneItLacks_IsExecute(string from, string to) =>
        FieldServiceCrossOrganizationAccess.PushedAction(FieldWork, from, to).Should().Be(OrderAction.Execute);

    [Theory]
    [InlineData(ServiceOrderStatus.InProgress, ServiceOrderStatus.Completed, "Complete")]
    [InlineData(ServiceOrderStatus.Pending, ServiceOrderStatus.Cancelled, "Cancel")]
    [InlineData(ServiceOrderStatus.Pending, ServiceOrderStatus.InProgress, "Execute")]
    public void ATypeWithoutAGraph_KnowsTheTerminalMoves(string from, string to, string code)
    {
        var expected = new OrderAction(code);
        FieldServiceCrossOrganizationAccess.PushedAction(null, from, to).Should().Be(expected);
        FieldServiceCrossOrganizationAccess.PushedAction(new OrderType { Id = "plain", DisplayName = "Plain" }, from, to).Should().Be(expected);
    }

    [Fact]
    public void AnUpdateThatKeepsTheStatus_IsAnExecutionUpdate() =>
        FieldServiceCrossOrganizationAccess.PushedAction(FieldWork, FieldWorkStatus.InProgress, FieldWorkStatus.InProgress).Should().Be(OrderAction.Execute);
}
