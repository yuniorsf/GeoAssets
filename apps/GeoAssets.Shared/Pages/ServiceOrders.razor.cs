using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;

namespace GeoAssets.Shared.Pages;

public partial class ServiceOrders
{
    private WorkflowPrincipal? _principal;
    private IServiceOrder? _selectedOrder;
    private bool _showCreateForm;

    protected override async Task OnInitializedAsync()
    {
        _principal = await PrincipalFactory.CreateAsync();
    }

    private void OnOrderSelected(IServiceOrder order)
    {
        _selectedOrder   = order;
        _showCreateForm  = false;
        StateHasChanged();
    }

    private void OnCreateRequested()
    {
        _selectedOrder  = null;
        _showCreateForm = true;
        StateHasChanged();
    }

    private void OnOrderCreated(IServiceOrder order)
    {
        _showCreateForm = false;
        _selectedOrder  = order;
        StateHasChanged();
    }

    private async Task ReloadSelectedAsync()
    {
        if (_selectedOrder is null) return;
        _selectedOrder = await Repository.GetByIdAsync(_selectedOrder.Id);
        StateHasChanged();
    }
}
