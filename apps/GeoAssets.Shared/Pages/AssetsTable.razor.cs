using GeoAssets.Core.Models;
using Microsoft.AspNetCore.Components;

namespace GeoAssets.Shared.Pages;

public partial class AssetsTable
{
    public const int DefaultPageSize = 50;
    private static readonly int[] PageSizeOptions = [25, 50, 100];

    private string _searchText = string.Empty;
    private string _assetTypeFilter = string.Empty;
    private string? _sortBy;
    private bool _sortDescending;
    private int _pageIndex;
    private int _pageSize = DefaultPageSize;
    private bool _loading;

    private IReadOnlyList<AssetType> _assetTypes = [];
    private PagedResult<GeoFeature>? _result;

    protected override async Task OnInitializedAsync()
    {
        _assetTypes = Repository.GetAssetTypes();
        await LoadPageAsync();
    }

    private async Task LoadPageAsync()
    {
        _loading = true;
        var query = BuildQuery(_searchText, _assetTypeFilter, _sortBy, _sortDescending, _pageIndex, _pageSize);
        _result = await Repository.GetPageAsync(query);
        _loading = false;
    }

    /// <summary>Pure query-building logic, factored out so it's directly unit-testable without rendering.</summary>
    public static AssetQuery BuildQuery(
        string searchText, string assetTypeFilter, string? sortBy, bool sortDescending, int pageIndex, int pageSize) =>
        new()
        {
            SearchText     = string.IsNullOrWhiteSpace(searchText) ? null : searchText,
            AssetTypeId    = string.IsNullOrEmpty(assetTypeFilter) ? null : assetTypeFilter,
            SortBy         = sortBy,
            SortDescending = sortDescending,
            Skip           = pageIndex * pageSize,
            Take           = pageSize,
        };

    /// <summary>Clicking the currently-sorted column reverses direction; clicking a different one sorts ascending.</summary>
    public static (string? SortBy, bool SortDescending) NextSort(
        string? currentSortBy, bool currentDescending, string clickedColumn) =>
        currentSortBy == clickedColumn
            ? (clickedColumn, !currentDescending)
            : (clickedColumn, false);

    public static int ComputeTotalPages(int totalCount, int pageSize) =>
        pageSize <= 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize);

    private bool CanGoPrevious => _pageIndex > 0;
    private bool CanGoNext => _pageIndex < ComputeTotalPages(_result?.TotalCount ?? 0, _pageSize) - 1;

    private async Task OnSearch(string query)
    {
        _searchText = query;
        _pageIndex = 0;
        await LoadPageAsync();
    }

    private async Task OnTypeFilterChanged(ChangeEventArgs e)
    {
        _assetTypeFilter = e.Value?.ToString() ?? string.Empty;
        _pageIndex = 0;
        await LoadPageAsync();
    }

    private async Task OnPageSizeChanged(ChangeEventArgs e)
    {
        _pageSize = int.TryParse(e.Value?.ToString(), out var size) ? size : DefaultPageSize;
        _pageIndex = 0;
        await LoadPageAsync();
    }

    private async Task OnSort(string column)
    {
        (_sortBy, _sortDescending) = NextSort(_sortBy, _sortDescending, column);
        _pageIndex = 0;
        await LoadPageAsync();
    }

    private async Task GoToPreviousPage()
    {
        if (!CanGoPrevious) return;
        _pageIndex--;
        await LoadPageAsync();
    }

    private async Task GoToNextPage()
    {
        if (!CanGoNext) return;
        _pageIndex++;
        await LoadPageAsync();
    }

    private string AssetTypeName(string assetTypeId) =>
        _assetTypes.FirstOrDefault(t => t.Id.ToString() == assetTypeId)?.Name ?? assetTypeId;

    private string SortIndicator(string column) =>
        _sortBy != column ? string.Empty : (_sortDescending ? " ▼" : " ▲");

    private string PagerSummary()
    {
        var total      = _result?.TotalCount ?? 0;
        var totalPages = Math.Max(ComputeTotalPages(total, _pageSize), 1);
        var page       = total == 0 ? 0 : _pageIndex + 1;
        return L.GetString("assetsTable.pagerSummary", _result?.Items.Count ?? 0, total, page, totalPages);
    }
}
