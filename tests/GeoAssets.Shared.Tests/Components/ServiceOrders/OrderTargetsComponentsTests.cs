using FluentAssertions;
using GeoAssets.Shared.Components.ServiceOrders;
using GeoAssets.Shared.Services.SharedUse;
using GeoAssets.Workflow.Targets;
using Xunit;

namespace GeoAssets.Shared.Tests.Components.ServiceOrders;

/// <summary>
/// The pure decisions of <see cref="ServiceOrderPager"/> and <see cref="OrderTargetsTable"/>
/// (XD01-312); this repo has no bUnit, so markup is covered through what it is built from.
/// </summary>
public sealed class OrderTargetsComponentsTests
{
    [Theory]
    // first page of three
    [InlineData(ServiceOrderPagerStep.First, 1, 25, 10, null)]
    [InlineData(ServiceOrderPagerStep.Previous, 1, 25, 10, null)]
    [InlineData(ServiceOrderPagerStep.Next, 1, 25, 10, 2)]
    [InlineData(ServiceOrderPagerStep.Last, 1, 25, 10, 3)]
    // middle page
    [InlineData(ServiceOrderPagerStep.First, 2, 25, 10, 1)]
    [InlineData(ServiceOrderPagerStep.Previous, 2, 25, 10, 1)]
    [InlineData(ServiceOrderPagerStep.Next, 2, 25, 10, 3)]
    [InlineData(ServiceOrderPagerStep.Last, 2, 25, 10, 3)]
    // last page
    [InlineData(ServiceOrderPagerStep.Previous, 3, 25, 10, 2)]
    [InlineData(ServiceOrderPagerStep.Next, 3, 25, 10, null)]
    [InlineData(ServiceOrderPagerStep.Last, 3, 25, 10, null)]
    // nothing to page
    [InlineData(ServiceOrderPagerStep.First, 1, 0, 10, null)]
    [InlineData(ServiceOrderPagerStep.Next, 1, 0, 10, null)]
    // a page left behind by rows that went away: back to the real last page
    [InlineData(ServiceOrderPagerStep.Previous, 7, 25, 10, 3)]
    [InlineData(ServiceOrderPagerStep.First, 7, 25, 10, 1)]
    [InlineData(ServiceOrderPagerStep.Next, 7, 25, 10, null)]
    [InlineData((ServiceOrderPagerStep)9, 2, 25, 10, null)]
    public void Pager_TargetPage_PerStepAndPosition(ServiceOrderPagerStep step, int page, int total, int pageSize, int? expected) =>
        ServiceOrderPager.TargetPage(step, page, total, pageSize).Should().Be(expected);

    [Fact]
    public void Pager_Steps_AreFirstPreviousNextLast_WithTheirLabels()
    {
        ServiceOrderPager.Steps.Should().Equal(ServiceOrderPagerStep.First, ServiceOrderPagerStep.Previous, ServiceOrderPagerStep.Next, ServiceOrderPagerStep.Last);
        ServiceOrderPager.Steps.Select(ServiceOrderPager.StepLabelKey).Should().Equal(
            "orders.pager.first", "assetsTable.previous", "assetsTable.next", "orders.pager.last");
    }

    [Fact]
    public void Table_Columns_SortByTheStoresKeys_InDisplayOrder() =>
        OrderTargetsTable.Columns.Select(c => c.SortKey).Should().Equal(
            OrderTargetSortKeys.Alias, OrderTargetSortKeys.FeatureId, OrderTargetSortKeys.Status,
            OrderTargetSortKeys.ConflictLevel, OrderTargetSortKeys.AddedAt);

    [Theory]
    [InlineData("None", OrderTargetConflictLevel.None)]
    [InlineData("Warning", OrderTargetConflictLevel.Warning)]
    [InlineData("Blocked", OrderTargetConflictLevel.Blocked)]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("warning", null)]
    [InlineData("2", null)]
    public void Table_ParseConflict_OnlyTheLevelsNames(string? value, OrderTargetConflictLevel? expected) =>
        OrderTargetsTable.ParseConflict(value).Should().Be(expected);

    [Theory]
    [InlineData("25", 25)]
    [InlineData("100", 100)]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("-5", null)]
    [InlineData("2.5", null)]
    [InlineData("ten", null)]
    public void Table_ParsePageSize_OnlyWholeNumbers(string? value, int? expected) =>
        OrderTargetsTable.ParsePageSize(value).Should().Be(expected);

    [Fact]
    public void Table_ActivitiesOf_NamesTheTargetsActivitiesFromTheCatalog()
    {
        OrderTargetActivity[] catalog = [new("remove-attachment", "Remove attachment"), new("relocate-attachment", "Relocate attachment")];
        var target = new ServiceOrderTarget
        {
            Id = "t1", OrderId = "o1", FeatureId = "f1", AssetTypeId = "pole", Status = OrderTargetStatus.Pending,
            AddedAt = new DateTime(2026, 11, 16, 0, 0, 0, DateTimeKind.Utc), AddedBy = "u1", OrganizationId = Guid.Empty,
            ActivityIds = ["relocate-attachment", "paint"],
        };

        OrderTargetsTable.ActivitiesOf(target, catalog).Should().Be("Relocate attachment, paint");
        OrderTargetsTable.ActivitiesOf(target, null).Should().Be("relocate-attachment, paint");
        OrderTargetsTable.ActivitiesOf(target with { ActivityIds = [] }, catalog).Should().Be(TargetActivityLabels.None);
        FluentActions.Invoking(() => OrderTargetsTable.ActivitiesOf(null!, catalog)).Should().Throw<ArgumentNullException>();
    }
}
