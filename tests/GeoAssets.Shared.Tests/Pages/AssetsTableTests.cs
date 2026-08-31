using FluentAssertions;
using GeoAssets.Shared.Pages;
using Xunit;

namespace GeoAssets.Shared.Tests.Pages;

/// <summary>
/// <see cref="AssetsTable"/>'s query-building, sort-toggling, and pagination math are factored
/// out as static methods specifically so they're directly unit-testable without a Blazor render
/// tree (this repo has no bUnit/component-rendering test infrastructure yet — see the pattern
/// already used by <c>MainLayout.ResolveOrganizationNameAsync</c> and
/// <c>NavMenu.FilterByPermissionAsync</c>).
/// </summary>
public class AssetsTableTests
{
    // ── BuildQuery ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BuildQuery_BlankSearchText_MapsToNullSearchText(string searchText)
    {
        var query = AssetsTable.BuildQuery(searchText, "", null, false, 0, 50);

        query.SearchText.Should().BeNull();
    }

    [Fact]
    public void BuildQuery_NonBlankSearchText_PassesThrough()
    {
        var query = AssetsTable.BuildQuery("tower", "", null, false, 0, 50);

        query.SearchText.Should().Be("tower");
    }

    [Fact]
    public void BuildQuery_EmptyAssetTypeFilter_MapsToNullAssetTypeId()
    {
        var query = AssetsTable.BuildQuery("", "", null, false, 0, 50);

        query.AssetTypeId.Should().BeNull();
    }

    [Fact]
    public void BuildQuery_NonEmptyAssetTypeFilter_PassesThrough()
    {
        var query = AssetsTable.BuildQuery("", "type-a", null, false, 0, 50);

        query.AssetTypeId.Should().Be("type-a");
    }

    [Theory]
    [InlineData(0, 50, 0)]
    [InlineData(1, 50, 50)]
    [InlineData(3, 25, 75)]
    public void BuildQuery_ComputesSkipFromPageIndexAndPageSize(int pageIndex, int pageSize, int expectedSkip)
    {
        var query = AssetsTable.BuildQuery("", "", null, false, pageIndex, pageSize);

        query.Skip.Should().Be(expectedSkip);
        query.Take.Should().Be(pageSize);
    }

    [Fact]
    public void BuildQuery_SortByAndSortDescending_PassThroughUnchanged()
    {
        var query = AssetsTable.BuildQuery("", "", "createdAt", true, 0, 50);

        query.SortBy.Should().Be("createdAt");
        query.SortDescending.Should().BeTrue();
    }

    // ── NextSort ──────────────────────────────────────────────────────────────

    [Fact]
    public void NextSort_NoCurrentSort_SortsClickedColumnAscending()
    {
        var (sortBy, descending) = AssetsTable.NextSort(currentSortBy: null, currentDescending: false, clickedColumn: "name");

        sortBy.Should().Be("name");
        descending.Should().BeFalse();
    }

    [Fact]
    public void NextSort_DifferentColumnClicked_SwitchesToAscendingOnNewColumn()
    {
        var (sortBy, descending) = AssetsTable.NextSort(currentSortBy: "name", currentDescending: true, clickedColumn: "createdAt");

        sortBy.Should().Be("createdAt");
        descending.Should().BeFalse();
    }

    [Fact]
    public void NextSort_SameColumnClickedAgainWhileAscending_TogglesToDescending()
    {
        var (sortBy, descending) = AssetsTable.NextSort(currentSortBy: "name", currentDescending: false, clickedColumn: "name");

        sortBy.Should().Be("name");
        descending.Should().BeTrue();
    }

    [Fact]
    public void NextSort_SameColumnClickedAgainWhileDescending_TogglesBackToAscending()
    {
        var (sortBy, descending) = AssetsTable.NextSort(currentSortBy: "name", currentDescending: true, clickedColumn: "name");

        sortBy.Should().Be("name");
        descending.Should().BeFalse();
    }

    // ── ComputeTotalPages ─────────────────────────────────────────────────────

    [Fact]
    public void ComputeTotalPages_ZeroRows_ReturnsZeroPages()
    {
        AssetsTable.ComputeTotalPages(totalCount: 0, pageSize: 50).Should().Be(0);
    }

    [Fact]
    public void ComputeTotalPages_ExactMultipleOfPageSize_DoesNotOverCount()
    {
        AssetsTable.ComputeTotalPages(totalCount: 100, pageSize: 50).Should().Be(2);
    }

    [Fact]
    public void ComputeTotalPages_RemainderRows_RoundsUpToExtraPage()
    {
        // Proves ceiling division, not truncating integer division (101/50 would wrongly be 2).
        AssetsTable.ComputeTotalPages(totalCount: 101, pageSize: 50).Should().Be(3);
    }

    [Fact]
    public void ComputeTotalPages_ZeroPageSize_ReturnsZeroInsteadOfDividingByZero()
    {
        AssetsTable.ComputeTotalPages(totalCount: 100, pageSize: 0).Should().Be(0);
    }
}
