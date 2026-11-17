using GeoAssets.Shared.Services.Occupancy;
using Microsoft.AspNetCore.Components;

namespace GeoAssets.Shared.Components.ServiceOrders;

/// <summary>
/// The pager of the order screens' paged lists (XD01-312): first / previous / next / last in a
/// labelled <c>nav</c>, unavailable steps <c>disabled</c> and <c>aria-disabled</c>, and a polite
/// live summary ("Showing n of N — page p of P"). Stateless: the page comes down, the requested
/// page goes up. Pages are 1-based.
/// </summary>
public partial class ServiceOrderPager
{
    /// <summary>The steps, in display order.</summary>
    public static IReadOnlyList<ServiceOrderPagerStep> Steps { get; } =
        [ServiceOrderPagerStep.First, ServiceOrderPagerStep.Previous, ServiceOrderPagerStep.Next, ServiceOrderPagerStep.Last];

    /// <summary>The current 1-based page.</summary>
    [Parameter] public int Page { get; set; } = 1;

    [Parameter] public int PageSize { get; set; } = 1;

    /// <summary>How many items there are in all.</summary>
    [Parameter] public int TotalCount { get; set; }

    /// <summary>How many items the current page shows.</summary>
    [Parameter] public int ItemCount { get; set; }

    /// <summary>Localization key of the <c>nav</c>'s accessible name.</summary>
    [Parameter, EditorRequired] public string LabelKey { get; set; } = null!;

    /// <summary>Raised with the page a step leads to; never for a disabled step.</summary>
    [Parameter] public EventCallback<int> OnPageRequested { get; set; }

    private bool IsEnabled(ServiceOrderPagerStep step) => TargetPage(step, Page, TotalCount, PageSize) is not null;

    private Task RequestAsync(ServiceOrderPagerStep step) =>
        TargetPage(step, Page, TotalCount, PageSize) is { } page ? OnPageRequested.InvokeAsync(page) : Task.CompletedTask;

    private string Summary() =>
        L.GetString("assetsTable.pagerSummary", ItemCount, TotalCount, TotalCount == 0 ? 0 : Page, Math.Max(OrderTargetPaging.TotalPages(TotalCount, PageSize), 1));

    /// <summary>
    /// The page <paramref name="step"/> leads to from <paramref name="page"/>, or null when the
    /// step is unavailable (already on the first or last page, or nothing to page).
    /// </summary>
    public static int? TargetPage(ServiceOrderPagerStep step, int page, int totalCount, int pageSize)
    {
        var last = OrderTargetPaging.TotalPages(totalCount, pageSize);
        return step switch
        {
            ServiceOrderPagerStep.First or ServiceOrderPagerStep.Previous when !OrderTargetPaging.HasPrevious(page) => null,
            ServiceOrderPagerStep.Next or ServiceOrderPagerStep.Last when !OrderTargetPaging.HasNext(page, totalCount, pageSize) => null,
            ServiceOrderPagerStep.First => 1,
            ServiceOrderPagerStep.Previous => Math.Min(page - 1, Math.Max(last, 1)),
            ServiceOrderPagerStep.Next => page + 1,
            ServiceOrderPagerStep.Last => last,
            _ => null,
        };
    }

    /// <summary>The label key of a step.</summary>
    public static string StepLabelKey(ServiceOrderPagerStep step) => step switch
    {
        ServiceOrderPagerStep.First => "orders.pager.first",
        ServiceOrderPagerStep.Previous => "assetsTable.previous",
        ServiceOrderPagerStep.Next => "assetsTable.next",
        _ => "orders.pager.last",
    };
}
