using GeoAssets.IpBilling.Fiscalizacao;
using GeoAssets.IpBilling.Workflow;
using GeoAssets.Workflow.Orders;
using Microsoft.AspNetCore.Components;

namespace GeoAssets.IpBilling.UI.Components;

/// <summary>
/// Unlike <c>LevantamentoDetail</c>, this does not re-load its totals from a GET endpoint on
/// <see cref="OnParametersSetAsync"/> — Fiscalização has no memória-de-cálculo accessor
/// (XD01-183's own decision). The totals/line table shown here only reflects the current
/// browser session's most recent Calculate/Validate action; re-selecting an already-calculated
/// order from the list after a page reload shows no totals until Calculate is run again
/// (safe: <see cref="IFiscalizacaoCalculationService.CalculateAsync"/> is an idempotent
/// recompute while already <c>Calculado</c>). Flagged as a known UX gap, not fixed here since
/// closing it would mean adding a REST endpoint, which is out of this ticket's scope.
/// </summary>
public partial class FiscalizacaoDetail
{
    [Parameter, EditorRequired] public IServiceOrder Order { get; set; } = null!;
    [Parameter] public EventCallback OnClose { get; set; }
    [Parameter] public EventCallback OnChanged { get; set; }

    [Inject] private IFiscalizacaoRestClient Client { get; set; } = null!;

    private int? _totalAgregado;
    private int? _totalRetroativo;
    private List<FiscalizacaoLine> _lines = [];
    private FiscalizacaoValidationResult? _posting;
    private bool _busy;
    private string? _error;
    private string? _loadedForOrderId;

    protected override Task OnParametersSetAsync()
    {
        if (string.Equals(_loadedForOrderId, Order.Id, StringComparison.Ordinal)) return Task.CompletedTask;
        _loadedForOrderId = Order.Id;
        _totalAgregado = null;
        _totalRetroativo = null;
        _lines = [];
        _posting = null;
        _error = null;

        return string.Equals(Order.Status, IpBillingWorkflowGraph.Validado, StringComparison.Ordinal)
            ? LoadPostingAsync()
            : Task.CompletedTask;
    }

    /// <summary>
    /// Re-reads what an already-<c>Validado</c> order posted (XD01-301). Validating a validated
    /// order is a pure report on the server — it never posts again and appends no action — so
    /// this is how the posted kWh and the provisional badge survive a page reload without a
    /// dedicated read endpoint.
    /// </summary>
    private async Task LoadPostingAsync()
    {
        try
        {
            var result = await Client.ValidateAsync(Order.Id, performedBy: null);
            _posting = result;
            _totalAgregado = result.TotalAgregado;
            _totalRetroativo = result.TotalRetroativo;
        }
        catch (Exception ex)
        {
            _error = ex.Message;
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
            var result = await Client.CalculateAsync(Order.Id, performedBy: null);
            _totalAgregado = result.TotalAgregado;
            _totalRetroativo = result.TotalRetroativo;
            _lines = [.. result.Lines];
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
            var result = await Client.ValidateAsync(Order.Id, performedBy: null);
            _posting = result;
            _totalAgregado = result.TotalAgregado;
            _totalRetroativo = result.TotalRetroativo;
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
