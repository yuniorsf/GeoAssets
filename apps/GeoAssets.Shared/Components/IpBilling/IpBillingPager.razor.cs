using Microsoft.AspNetCore.Components;

namespace GeoAssets.Shared.Components.IpBilling;

/// <summary>
/// Previous/next pager shared by the paged IP-billing admin tables (XD01-301) — the same
/// markup, labels and summary as <c>AssetsTable</c>'s pager, factored into a component because
/// the Distribuidoras page needs it twice (the Distribuidora list and its lamp chart).
/// <see cref="Page"/> is 1-based, matching the IP-billing repositories' paging contract.
/// </summary>
public partial class IpBillingPager
{
    [Parameter, EditorRequired] public int Page { get; set; }
    [Parameter, EditorRequired] public int PageSize { get; set; }
    [Parameter, EditorRequired] public int TotalCount { get; set; }
    [Parameter] public int ItemCount { get; set; }
    [Parameter] public string AriaLabel { get; set; } = string.Empty;
    [Parameter] public EventCallback<int> OnPageChanged { get; set; }

    /// <summary>Number of pages needed for <paramref name="totalCount"/> rows; 0 when there are none.</summary>
    public static int ComputeTotalPages(int totalCount, int pageSize) =>
        pageSize <= 0 || totalCount <= 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize);

    /// <summary>
    /// The page to show after a reload: <paramref name="page"/> clamped into
    /// <c>[1, totalPages]</c>, so deleting the last row of the last page steps back instead of
    /// stranding the user on an empty page.
    /// </summary>
    public static int ClampPage(int page, int totalCount, int pageSize) =>
        Math.Clamp(page, 1, Math.Max(ComputeTotalPages(totalCount, pageSize), 1));

    private bool CanGoPrevious => Page > 1;
    private bool CanGoNext => Page < ComputeTotalPages(TotalCount, PageSize);

    private Task GoToPreviousAsync() => CanGoPrevious ? OnPageChanged.InvokeAsync(Page - 1) : Task.CompletedTask;

    private Task GoToNextAsync() => CanGoNext ? OnPageChanged.InvokeAsync(Page + 1) : Task.CompletedTask;

    private string Summary()
    {
        var totalPages = Math.Max(ComputeTotalPages(TotalCount, PageSize), 1);
        var page = TotalCount == 0 ? 0 : Page;
        return L.GetString("assetsTable.pagerSummary", ItemCount, TotalCount, page, totalPages);
    }
}
