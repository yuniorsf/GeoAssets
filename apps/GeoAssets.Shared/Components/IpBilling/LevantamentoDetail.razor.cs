using GeoAssets.IpBilling.Levantamento;
using GeoAssets.IpBilling.Workflow;
using GeoAssets.Workflow.Orders;
using Microsoft.AspNetCore.Components;

namespace GeoAssets.Shared.Components.IpBilling;

public partial class LevantamentoDetail
{
    [Parameter, EditorRequired] public IServiceOrder Order { get; set; } = null!;
    [Parameter] public EventCallback OnClose { get; set; }
    [Parameter] public EventCallback OnChanged { get; set; }

    [Inject] private ILevantamentoRestClient Client { get; set; } = null!;

    private LevantamentoMemoriaCalculoDto? _memoria;
    private bool _busy;
    private string? _error;
    private string? _loadedForOrderId;

    protected override async Task OnParametersSetAsync()
    {
        if (string.Equals(_loadedForOrderId, Order.Id, StringComparison.Ordinal)) return;
        _loadedForOrderId = Order.Id;
        _memoria = null;

        if (!string.Equals(Order.Status, IpBillingWorkflowGraph.Carregado, StringComparison.Ordinal))
        {
            await LoadMemoriaAsync();
        }
    }

    private async Task LoadMemoriaAsync()
    {
        try
        {
            _memoria = await Client.GetMemoriaCalculoAsync(Order.Id);
        }
        catch (Exception)
        {
            // No calculation persisted yet (e.g. still Carregado with no upload) — leave the
            // table hidden rather than surfacing a confusing error for an expected empty state.
            _memoria = null;
        }
    }

    private Task HandleUploaded() => OnChanged.InvokeAsync();

    private async Task HandleCalculateAsync()
    {
        _busy = true;
        _error = null;
        StateHasChanged();

        try
        {
            await Client.CalculateAsync(Order.Id, asOf: null, performedBy: null);
            await LoadMemoriaAsync();
            await OnChanged.InvokeAsync();
        }
        catch (Exception ex)
        {
            _error = ex.Message;
        }
        finally
        {
            _busy = false;
            StateHasChanged();
        }
    }

    private async Task HandleValidateAsync()
    {
        _busy = true;
        _error = null;
        StateHasChanged();

        try
        {
            await Client.ValidateAsync(Order.Id, performedBy: null);
            await LoadMemoriaAsync();
            await OnChanged.InvokeAsync();
        }
        catch (Exception ex)
        {
            _error = ex.Message;
        }
        finally
        {
            _busy = false;
            StateHasChanged();
        }
    }
}
