using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using Microsoft.AspNetCore.Components;

namespace GeoAssets.Shared.Pages;

public partial class ServiceOrders
{
    private WorkflowPrincipal? _principal;
    private IServiceOrder? _selectedOrder;
    private bool _showCreateForm;
    private bool _createFromSelection;
    private bool _fromSelectionHandled;

    /// <summary>The address the map's "Create order" button opens (XD01-312).</summary>
    public const string CreateFromSelectionUri = "service-orders?" + FromSelectionQueryName + "=true";

    private const string FromSelectionQueryName = "fromSelection";

    /// <summary>Set by <see cref="CreateFromSelectionUri"/>: open the create form with the map selection as targets.</summary>
    [SupplyParameterFromQuery(Name = FromSelectionQueryName)] public bool FromSelection { get; set; }

    protected override async Task OnInitializedAsync()
    {
        _principal = await PrincipalFactory.CreateAsync();
    }

    protected override void OnParametersSet()
    {
        if (!FromSelection)
        {
            _fromSelectionHandled = false;
            return;
        }

        if (_fromSelectionHandled) return;

        _fromSelectionHandled = true;
        _selectedOrder        = null;
        _createFromSelection  = true;
        _showCreateForm       = true;
    }

    private void OnOrderSelected(IServiceOrder order)
    {
        _selectedOrder   = order;
        _showCreateForm  = false;
        StateHasChanged();
    }

    private void OnCreateRequested()
    {
        _selectedOrder       = null;
        _createFromSelection = false;
        _showCreateForm      = true;
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
