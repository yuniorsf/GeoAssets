using FluentAssertions;
using GeoAssets.Shared.Services.OrderTargets;
using GeoAssets.Workflow.Targets;
using Xunit;

namespace GeoAssets.Shared.Tests.Services.OrderTargets;

/// <summary>The targets grid's paging, sorting and filtering state (XD01-312).</summary>
public sealed class OrderTargetsGridStateTests
{
    private static OrderTargetsGridState OnPage3() =>
        OrderTargetsGridState.Default.WithPageSize(10).WithPage(3, totalCount: 95);

    [Fact]
    public void Default_IsTheFirstPageOf25_ByFeatureIdAscending_Unfiltered()
    {
        var query = OrderTargetsGridState.Default.ToQuery("o1");

        query.Should().Be(new OrderTargetQuery("o1", 1, 25, OrderTargetSortKeys.FeatureId, false, null, null));
        OrderTargetsGridState.PageSizeOptions.Should().Equal(10, 25, 50, 100);
        OrderTargetsGridState.PageSizeOptions.Should().OnlyContain(size => size <= OrderTargetQuery.MaxPageSize);
    }

    [Fact]
    public void ToQuery_CarriesEverySetting()
    {
        var state = OrderTargetsGridState.Default.WithPageSize(50).WithSort(OrderTargetSortKeys.AddedAt).WithSort(OrderTargetSortKeys.AddedAt)
            .WithStatus(OrderTargetStatus.Done).WithConflict(OrderTargetConflictLevel.Blocked).WithPage(2, totalCount: 120);

        state.ToQuery("o1").Should().Be(new OrderTargetQuery("o1", 2, 50, OrderTargetSortKeys.AddedAt, true, OrderTargetStatus.Done, OrderTargetConflictLevel.Blocked));
    }

    [Fact]
    public void ToQuery_BlankOrderId_Throws() =>
        FluentActions.Invoking(() => OrderTargetsGridState.Default.ToQuery(" ")).Should().Throw<ArgumentException>();

    [Fact]
    public void WithSort_NewColumnAscending_SameColumnToggles_UnknownSortsByFeatureId()
    {
        var byStatus = OrderTargetsGridState.Default.WithSort(OrderTargetSortKeys.Status);
        byStatus.Should().Match<OrderTargetsGridState>(s => s.SortBy == OrderTargetSortKeys.Status && !s.Descending);

        byStatus.WithSort(OrderTargetSortKeys.Status).Descending.Should().BeTrue();
        byStatus.WithSort(OrderTargetSortKeys.Status).WithSort(OrderTargetSortKeys.Status).Descending.Should().BeFalse();
        byStatus.WithSort(OrderTargetSortKeys.Status).WithSort(OrderTargetSortKeys.Alias).Descending.Should().BeFalse();

        var unknown = byStatus.WithSort("color");
        unknown.SortBy.Should().Be(OrderTargetSortKeys.FeatureId);
        unknown.Descending.Should().BeFalse();
        OrderTargetsGridState.Default.WithSort("color").Descending.Should().BeTrue("an unknown key is the feature id, which is already the key");
    }

    [Fact]
    public void SortFilterOrSizeChange_ReturnsToTheFirstPage()
    {
        OnPage3().Page.Should().Be(3);

        OnPage3().WithSort(OrderTargetSortKeys.Alias).Page.Should().Be(1);
        OnPage3().WithStatus(OrderTargetStatus.Pending).Page.Should().Be(1);
        OnPage3().WithConflict(OrderTargetConflictLevel.Warning).Page.Should().Be(1);
        OnPage3().WithPageSize(25).Page.Should().Be(1);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("pending")]
    [InlineData("Elsewhere")]
    public void WithStatus_BlankOrUndefined_ShowsEveryStatus(string? status) =>
        OrderTargetsGridState.Default.WithStatus(OrderTargetStatus.Done).WithStatus(status).Status.Should().BeNull();

    [Fact]
    public void WithConflict_NullOrUndefined_ShowsEveryLevel()
    {
        var filtered = OrderTargetsGridState.Default.WithConflict(OrderTargetConflictLevel.None);
        filtered.Conflict.Should().Be(OrderTargetConflictLevel.None);

        filtered.WithConflict(null).Conflict.Should().BeNull();
        filtered.WithConflict((OrderTargetConflictLevel)9).Conflict.Should().BeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(200)]
    public void WithPageSize_NotOffered_KeepsTheStateAsItWas(int size) =>
        OnPage3().WithPageSize(size).Should().Be(OnPage3());

    [Theory]
    [InlineData(0, 95, 1)]
    [InlineData(10, 95, 10)]
    [InlineData(11, 95, 10)]
    [InlineData(4, 0, 1)]
    public void WithPage_IsClampedToTheExistingPages(int page, int total, int expected) =>
        OrderTargetsGridState.Default.WithPageSize(10).WithPage(page, total).Page.Should().Be(expected);

    [Fact]
    public void AriaSort_NoneAscendingDescending()
    {
        var state = OrderTargetsGridState.Default.WithSort(OrderTargetSortKeys.Alias);

        state.AriaSort(OrderTargetSortKeys.Alias).Should().Be("ascending");
        state.WithSort(OrderTargetSortKeys.Alias).AriaSort(OrderTargetSortKeys.Alias).Should().Be("descending");
        state.AriaSort(OrderTargetSortKeys.Status).Should().Be("none");
        state.AriaSort("color").Should().Be("none");
    }
}
