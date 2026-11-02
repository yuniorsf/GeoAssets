using GeoAssets.IpBilling.Workflow;
using GeoAssets.Workflow.Orders;
using Microsoft.AspNetCore.Components;

namespace GeoAssets.Shared.Components.IpBilling;

public partial class LevantamentoList
{
    [Parameter] public string? SelectedOrderId { get; set; }
    [Parameter] public EventCallback<IServiceOrder> OnOrderSelected { get; set; }
    [Parameter] public EventCallback OnCreateRequested { get; set; }

    private List<IServiceOrder> _orders = [];
    private bool _loading = true;

    protected override Task OnInitializedAsync() => RefreshAsync();

    public async Task RefreshAsync()
    {
        _loading = true;
        StateHasChanged();
        _orders = [.. (await Repository.GetByOrderTypeAsync(IpBillingOrderTypes.LevantamentoId))
            .OrderByDescending(o => o.CreatedAt)];
        _loading = false;
        StateHasChanged();
    }

    private Task SelectOrder(IServiceOrder order) => OnOrderSelected.InvokeAsync(order);
}
