using GeoAssets.IpBilling.Oficio;
using GeoAssets.Workflow.Orders;
using Microsoft.AspNetCore.Components;

namespace GeoAssets.Shared.Components.IpBilling;

/// <summary>
/// Unlike <c>LevantamentoDetail</c>, this does not re-load its totals from a GET endpoint on
/// <see cref="OnParametersSetAsync"/> — Ofício has no memória-de-cálculo accessor, same decision
/// as Fiscalização's (XD01-183). The totals/line/swap table shown here only reflects the
/// current browser session's most recent Calculate action; re-selecting an already-calculated
/// order from the list after a page reload shows no totals until Calculate is run again (safe:
/// <see cref="IOficioCalculationService.CalculateStoredAsync"/> is an idempotent recompute while
/// already <c>Calculado</c>). Same known UX gap as Fiscalização's, not fixed here for the same
/// reason (closing it needs a new REST endpoint, out of this ticket's scope).
/// </summary>
public partial class OficioDetail
{
    [Parameter, EditorRequired] public IServiceOrder Order { get; set; } = null!;
    [Parameter] public EventCallback OnClose { get; set; }
    [Parameter] public EventCallback OnChanged { get; set; }

    [Inject] private IOficioRestClient Client { get; set; } = null!;

    private int? _totalAgregado;
    private int? _totalRetroativo;
    private List<OficioLine> _lines = [];
    private List<OficioLineSwapResult> _swaps = [];
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
        _swaps = [];
        return Task.CompletedTask;
    }

    /// <summary>Whether <paramref name="lineId"/> was flagged as a swap ("troca") by
    /// <c>OficioLineSwapView.Compute</c> (XD01-185) in the most recently-fetched
    /// <paramref name="swaps"/> list. Extracted as a pure, testable method since this repo has
    /// no bUnit-style component-rendering test convention yet.</summary>
    public static bool IsLineSwapped(IReadOnlyList<OficioLineSwapResult> swaps, Guid lineId) =>
        swaps.Any(s => s.LineId == lineId && s.IsSwap);

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
            _swaps = [.. result.Swaps];
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
