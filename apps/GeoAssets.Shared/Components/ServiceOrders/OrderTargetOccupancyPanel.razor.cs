using GeoAssets.Shared.Services.Occupancy;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using GeoAssets.Workflow.Targets;
using Microsoft.AspNetCore.Components;

namespace GeoAssets.Shared.Components.ServiceOrders;

/// <summary>
/// The smallest way into <see cref="OccupancyChangeEditor"/> from an order (XD01-315): a compact,
/// paged list of the order's targets — <see cref="OrderTargetPaging.PageSize"/> per request,
/// in the target store's default order — each with a button that opens the editor for that
/// pole beneath the list. It is not the targets grid (filters, sorting, activities, selection):
/// that belongs to the slice that builds it, which can host the editor the same way.
/// </summary>
public partial class OrderTargetOccupancyPanel
{
    private readonly CancellationTokenSource _lifetime = new();

    private string? _loadedOrderId;
    private IReadOnlyList<ServiceOrderTarget>? _targets;
    private int _totalCount;
    private int _page = 1;
    private bool _loadFailed;
    private ServiceOrderTarget? _selected;

    [Parameter, EditorRequired] public IServiceOrder Order { get; set; } = null!;
    [Parameter, EditorRequired] public WorkflowPrincipal Principal { get; set; } = null!;

    /// <summary>Whether the editor offers editing controls; the host still decides what is allowed.</summary>
    [Parameter] public bool CanEdit { get; set; }

    private bool CanGoPrevious => OrderTargetPaging.HasPrevious(_page);

    private bool CanGoNext => OrderTargetPaging.HasNext(_page, _totalCount, OrderTargetPaging.PageSize);

    protected override async Task OnParametersSetAsync()
    {
        if (string.Equals(Order.Id, _loadedOrderId, StringComparison.Ordinal)) return;

        _loadedOrderId = Order.Id;
        (_page, _selected) = (1, null);
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        (_targets, _loadFailed) = (null, false);
        try
        {
            var result = await Targets.GetPageAsync(
                new OrderTargetQuery(Order.Id, _page, OrderTargetPaging.PageSize), _lifetime.Token);
            (_targets, _totalCount) = (result.Items, result.TotalCount);
        }
        catch (OperationCanceledException)
        {
            // The component went away while loading.
        }
        catch (Exception)
        {
            _loadFailed = true;
        }
    }

    private async Task GoToAsync(int page)
    {
        var clamped = OrderTargetPaging.Clamp(page, _totalCount, OrderTargetPaging.PageSize);
        if (clamped == _page) return;

        _page = clamped;
        await LoadAsync();
    }

    private void Select(ServiceOrderTarget target) =>
        _selected = string.Equals(_selected?.Id, target.Id, StringComparison.Ordinal) ? null : target;

    private void CloseEditor() => _selected = null;

    private string Summary() => L.GetString(
        "assetsTable.pagerSummary",
        _targets?.Count ?? 0,
        _totalCount,
        _totalCount == 0 ? 0 : _page,
        Math.Max(OrderTargetPaging.TotalPages(_totalCount, OrderTargetPaging.PageSize), 1));

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _lifetime.Cancel();
            _lifetime.Dispose();
        }

        base.Dispose(disposing);
    }
}
