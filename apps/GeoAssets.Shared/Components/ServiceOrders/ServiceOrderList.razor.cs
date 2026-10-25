using GeoAssets.Workflow.Orders;
using Microsoft.AspNetCore.Components;

namespace GeoAssets.Shared.Components.ServiceOrders;

public partial class ServiceOrderList
{
    [Parameter] public string? SelectedOrderId { get; set; }
    [Parameter] public EventCallback<IServiceOrder> OnOrderSelected { get; set; }
    [Parameter] public EventCallback OnCreateRequested { get; set; }

    private static readonly string[] _statuses =
    [
        ServiceOrderStatus.Draft, ServiceOrderStatus.Pending, ServiceOrderStatus.InProgress,
        ServiceOrderStatus.OnHold, ServiceOrderStatus.Completed, ServiceOrderStatus.Cancelled,
    ];

    private List<IServiceOrder> _all = [];
    private List<IServiceOrder> _filtered = [];
    private string _searchQuery = string.Empty;
    private string _statusFilter = string.Empty;

    protected override Task OnInitializedAsync()
    {
        Repository.OrderAdded        += OnChanged;
        Repository.OrderUpdated      += OnChanged;
        Repository.OrderStatusChanged += OnStatusChanged;
        Repository.OrderDeleted      += OnDeleted;
        return RefreshListAsync();
    }

    private void OnChanged(object? _, IServiceOrder __) => _ = ReloadAndRenderAsync();
    private void OnStatusChanged(object? _, (IServiceOrder Order, string Previous) __) => _ = ReloadAndRenderAsync();
    private void OnDeleted(object? _, string __) => _ = ReloadAndRenderAsync();

    private Task ReloadAndRenderAsync() =>
        InvokeAsync(async () =>
        {
            await RefreshListAsync();
            StateHasChanged();
        });

    private async Task RefreshListAsync()
    {
        _all = [.. await Repository.GetAllAsync()];
        ApplyFilters();
    }

    private void ApplyFilters()
    {
        IEnumerable<IServiceOrder> query = _all;

        if (!string.IsNullOrEmpty(_searchQuery))
            query = query.Where(o => o.Title.Contains(_searchQuery, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrEmpty(_statusFilter))
            query = query.Where(o => string.Equals(o.Status, _statusFilter, StringComparison.Ordinal));

        _filtered = [.. query.OrderByDescending(o => o.CreatedAt)];
    }

    private Task OnSearch(string query)
    {
        _searchQuery = query;
        ApplyFilters();
        return Task.CompletedTask;
    }

    private void OnStatusFilter(ChangeEventArgs e)
    {
        _statusFilter = e.Value?.ToString() ?? string.Empty;
        ApplyFilters();
    }

    private Task SelectOrder(IServiceOrder order) => OnOrderSelected.InvokeAsync(order);

    private Task RequestCreate() => OnCreateRequested.InvokeAsync();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Repository.OrderAdded        -= OnChanged;
            Repository.OrderUpdated      -= OnChanged;
            Repository.OrderStatusChanged -= OnStatusChanged;
            Repository.OrderDeleted      -= OnDeleted;
        }
        base.Dispose(disposing);
    }
}
