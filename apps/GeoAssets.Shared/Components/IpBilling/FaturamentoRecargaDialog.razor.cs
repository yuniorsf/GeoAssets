using GeoAssets.IpBilling.FaturamentoLedger;
using GeoAssets.IpBilling.Models;
using Microsoft.AspNetCore.Components;

namespace GeoAssets.Shared.Components.IpBilling;

public partial class FaturamentoRecargaDialog
{
    [Parameter] public bool Visible { get; set; }

    /// <summary>The ledger row being corrected — null until a row's "Recarga" action is
    /// clicked on <see cref="FaturamentoLedgerTable"/>.</summary>
    [Parameter] public Faturamento? Target { get; set; }
    [Parameter] public EventCallback<FaturamentoBatchResult> OnCompleted { get; set; }
    [Parameter] public EventCallback OnCancel { get; set; }
    [Inject] private IFaturamentoRestClient Client { get; set; } = null!;

    private decimal _kwhFaturado;
    private bool _submitting;
    private string? _error;
    private string? _resultMessage;

    protected override void OnParametersSet()
    {
        if (Visible && Target is not null)
        {
            _kwhFaturado = Target.KwhFaturado;
            _error = null;
            _resultMessage = null;
        }
    }

    private async Task HandleSubmitAsync()
    {
        if (Target is null) return;

        _submitting = true;
        _error = null;
        _resultMessage = null;
        StateHasChanged();

        try
        {
            var result = await Client.RecargaAsync(Target.MunicipioId, Target.Competencia, _kwhFaturado, performedBy: null);
            _resultMessage = FaturamentoBatchResultFormatter.Summarize(result);
            await OnCompleted.InvokeAsync(result);
        }
        catch (Exception ex)
        {
            _error = L.GetString("admin.ipBilling.faturamento.recargaError", ex.Message);
        }
        finally
        {
            _submitting = false;
        }
    }

    private Task Cancel() => OnCancel.InvokeAsync();
}
