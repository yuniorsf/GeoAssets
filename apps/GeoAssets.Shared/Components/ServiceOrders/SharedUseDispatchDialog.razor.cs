using GeoAssets.Shared.Services.Occupancy;
using GeoAssets.Shared.Services.SharedUse;
using GeoAssets.NeutralOperator.Application.SharedUse;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using Microsoft.AspNetCore.Components;

namespace GeoAssets.Shared.Components.ServiceOrders;

/// <summary>
/// The dispatch dialog of a shared-use order (XD01-316): instead of asking for one recipient it
/// shows who the order would be dispatched to — one order per occupant organization with an
/// active contract on the order's poles — what would be left out and why, and dispatches on
/// confirmation. Nothing is decided here: the plan comes from
/// <see cref="ISharedUseDispatchPlanReader"/>, the dispatch goes through
/// <see cref="ISharedUseDispatchService"/> with the row version the plan was computed against,
/// and <see cref="SharedUseDispatchDialogState"/> holds the dialog's own small decisions.
///
/// Both tables are paged (<see cref="SharedUseDispatchDialogState.PageSize"/> rows per request);
/// the list of attachments that get no order is fetched only when it is opened.
/// </summary>
public partial class SharedUseDispatchDialog
{
    private readonly CancellationTokenSource _lifetime = new();

    private string? _loadedOrderId;
    private bool _wasVisible;
    private SharedUseDispatchPreview? _preview;
    private SharedUseDispatchResult? _result;
    private IReadOnlyList<SharedUseSkippedEntry>? _skipped;
    private int _skippedTotal;
    private int _skippedPage = 1;
    private bool _skippedOpen;
    private bool _skippedFailed;
    private int _page = 1;
    private bool _loading;
    private bool _busy;
    private string? _errorKey;

    [Parameter] public bool Visible { get; set; }
    [Parameter, EditorRequired] public IServiceOrder Order { get; set; } = null!;
    [Parameter, EditorRequired] public WorkflowPrincipal Principal { get; set; } = null!;

    /// <summary>Raised after a dispatch that went through, so the host can reload the order.</summary>
    [Parameter] public EventCallback<SharedUseDispatchResult> OnDispatched { get; set; }

    [Parameter] public EventCallback OnCancel { get; set; }

    private bool CanConfirm => SharedUseDispatchDialogState.CanConfirm(_preview, _busy || _loading);

    private IReadOnlyList<SharedUseDispatchCaveat> Caveats => SharedUseDispatchDialogState.Caveats(_preview);

    private int RecipientTotal => _preview?.Recipients.TotalCount ?? 0;

    private bool CanGoToPreviousRecipients => !_loading && OrderTargetPaging.HasPrevious(_page);

    private bool CanGoToNextRecipients => !_loading && OrderTargetPaging.HasNext(_page, RecipientTotal, SharedUseDispatchDialogState.PageSize);

    private bool CanGoToPreviousSkipped => OrderTargetPaging.HasPrevious(_skippedPage);

    private bool CanGoToNextSkipped => OrderTargetPaging.HasNext(_skippedPage, _skippedTotal, SharedUseDispatchDialogState.PageSize);

    protected override async Task OnParametersSetAsync()
    {
        var opened = Visible && (!_wasVisible || !string.Equals(Order.Id, _loadedOrderId, StringComparison.Ordinal));
        _wasVisible = Visible;
        if (!opened) return;

        _loadedOrderId = Order.Id;
        Reset();
        await LoadPlanAsync();
    }

    private void Reset()
    {
        (_preview, _result, _errorKey) = (null, null, null);
        (_skipped, _skippedTotal, _skippedPage, _skippedOpen, _skippedFailed) = (null, 0, 1, false, false);
        _page = 1;
    }

    private async Task LoadPlanAsync()
    {
        _loading = true;
        try
        {
            _preview = await Plans.PreviewAsync(Order.Id, _page, SharedUseDispatchDialogState.PageSize, Principal, _lifetime.Token);
        }
        catch (OperationCanceledException)
        {
            // The component went away while loading.
        }
        catch (Exception ex)
        {
            _errorKey = SharedUseDispatchMessages.ErrorKey(ex);
        }
        finally
        {
            _loading = false;
        }
    }

    private async Task GoToRecipientsAsync(int page)
    {
        var clamped = OrderTargetPaging.Clamp(page, RecipientTotal, SharedUseDispatchDialogState.PageSize);
        if (clamped == _page || _loading) return;

        _page = clamped;
        await LoadPlanAsync();
    }

    private async Task ToggleSkippedAsync()
    {
        _skippedOpen = !_skippedOpen;
        if (_skippedOpen && _skipped is null) await LoadSkippedAsync();
    }

    private async Task GoToSkippedAsync(int page)
    {
        var clamped = OrderTargetPaging.Clamp(page, _skippedTotal, SharedUseDispatchDialogState.PageSize);
        if (clamped == _skippedPage) return;

        _skippedPage = clamped;
        await LoadSkippedAsync();
    }

    private async Task LoadSkippedAsync()
    {
        _skippedFailed = false;
        try
        {
            var page = await Plans.GetSkippedEntriesAsync(
                Order.Id, _skippedPage, SharedUseDispatchDialogState.PageSize, Principal, _lifetime.Token);
            (_skipped, _skippedTotal) = (page.Items, page.TotalCount);
        }
        catch (OperationCanceledException)
        {
            // The component went away while loading.
        }
        catch (Exception)
        {
            _skippedFailed = true;
        }
    }

    private async Task ConfirmAsync()
    {
        // A second click while the first request is still running does nothing.
        if (!CanConfirm || _preview is null) return;

        (_busy, _errorKey) = (true, null);
        try
        {
            _result = await Dispatcher.DispatchAsync(Order.Id, _preview.RowVersion, Principal, _lifetime.Token);
            _page = 1;
            await LoadPlanAsync();
            await OnDispatched.InvokeAsync(_result);
        }
        catch (OperationCanceledException)
        {
            // The component went away while dispatching.
        }
        catch (Exception ex)
        {
            _errorKey = SharedUseDispatchMessages.ErrorKey(ex);

            // Whatever went wrong, the plan on screen may be stale: show the current one.
            await LoadPlanAsync();
        }
        finally
        {
            _busy = false;
        }
    }

    private Task Cancel() => _busy ? Task.CompletedTask : OnCancel.InvokeAsync();

    private string RecipientsSummary() => PagerSummary(_preview?.Recipients.Items.Count ?? 0, RecipientTotal, _page);

    private string SkippedSummary() => PagerSummary(_skipped?.Count ?? 0, _skippedTotal, _skippedPage);

    private string PagerSummary(int shown, int total, int page) => L.GetString(
        "assetsTable.pagerSummary",
        shown,
        total,
        total == 0 ? 0 : page,
        Math.Max(OrderTargetPaging.TotalPages(total, SharedUseDispatchDialogState.PageSize), 1));

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
