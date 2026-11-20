using FluentAssertions;
using GeoAssets.NeutralOperator.Application.Occupancy.Saga;
using GeoAssets.Workflow.Orders;
using Xunit;

namespace GeoAssets.Workflow.Tests.Occupancy;

/// <summary><see cref="OccupancyOrderRevision.SagaAttributes"/> and the unrestored-poles attribute of XD01-392.</summary>
public sealed class OccupancyOrderRevisionTests
{
    private static ServiceOrder Order(params (string Key, string Value)[] attributes) => new()
    {
        Id = "ord-1",
        Title = "Pole work",
        OrderTypeId = "pole-work",
        Status = ServiceOrderStatus.Completed,
        CreatedBy = "planner-1",
        Attributes = attributes.ToDictionary(a => a.Key, a => a.Value, StringComparer.Ordinal),
    };

    [Fact]
    public void SagaAttributes_WithUnrestoredPoles_SetsTheKeyCommaJoinedWithoutSpaces_InTheGivenOrder()
    {
        var attributes = OccupancyOrderRevision.SagaAttributes(
            Order(), OccupancyApplySagaAttributes.Reverted, unrestored: ["pole-1", "pole-2", "pole-9"]);

        attributes.Should().Contain(OccupancyApplySagaAttributes.StateKey, OccupancyApplySagaAttributes.Reverted)
            .And.Contain(OccupancyApplySagaAttributes.UnrestoredKey, "pole-1,pole-2,pole-9");
    }

    [Fact]
    public void SagaAttributes_WithASingleUnrestoredPole_SetsItAloneWithoutASeparator()
    {
        OccupancyOrderRevision.SagaAttributes(Order(), OccupancyApplySagaAttributes.Reverted, unrestored: ["pole-1"])
            .Should().Contain(OccupancyApplySagaAttributes.UnrestoredKey, "pole-1");
    }

    [Fact]
    public void SagaAttributes_WithNoUnrestoredPoles_RemovesAStaleKey_AndKeepsTheRest()
    {
        var order = Order(
            (OccupancyApplySagaAttributes.UnrestoredKey, "pole-stale"),
            ("occupancy.other", "kept"));

        var empty = OccupancyOrderRevision.SagaAttributes(order, OccupancyApplySagaAttributes.AwaitingApply, Guid.NewGuid(), unrestored: []);
        var missing = OccupancyOrderRevision.SagaAttributes(order, OccupancyApplySagaAttributes.Applied);

        empty.Should().NotContainKey(OccupancyApplySagaAttributes.UnrestoredKey).And.Contain("occupancy.other", "kept");
        missing.Should().NotContainKey(OccupancyApplySagaAttributes.UnrestoredKey).And.Contain("occupancy.other", "kept");
    }

    [Fact]
    public void SagaAttributes_WithNoUnrestoredPolesAndNoStaleKey_AddsNothingForIt()
    {
        var batch = Guid.NewGuid();

        var attributes = OccupancyOrderRevision.SagaAttributes(Order(), OccupancyApplySagaAttributes.AwaitingApply, batch);

        attributes.Keys.Should().BeEquivalentTo([OccupancyApplySagaAttributes.StateKey, OccupancyApplySagaAttributes.BatchIdKey]);
        attributes[OccupancyApplySagaAttributes.BatchIdKey].Should().Be(batch.ToString("D"));
    }

    [Fact]
    public void SagaAttributes_ReplacesAStaleKeyWithTheNewList_AndDoesNotMutateTheOrder()
    {
        var order = Order((OccupancyApplySagaAttributes.UnrestoredKey, "pole-old"));

        var attributes = OccupancyOrderRevision.SagaAttributes(order, OccupancyApplySagaAttributes.Reverted, unrestored: ["pole-new"]);

        attributes.Should().Contain(OccupancyApplySagaAttributes.UnrestoredKey, "pole-new");
        order.Attributes.Should().Contain(OccupancyApplySagaAttributes.UnrestoredKey, "pole-old");
    }

    [Fact]
    public void UnrestoredKey_IsTheDocumentedAttributeName()
    {
        OccupancyApplySagaAttributes.UnrestoredKey.Should().Be("occupancy.apply.unrestored");
    }
}
