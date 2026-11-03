using System.ComponentModel.DataAnnotations;
using GeoAssets.IpBilling.FaturamentoLedger;
using GeoAssets.IpBilling.Models;
using Microsoft.AspNetCore.Components;

namespace GeoAssets.Shared.Components.IpBilling;

public partial class FaturamentoManualEntryDialog
{
    [Parameter] public bool Visible { get; set; }
    [Parameter] public IReadOnlyList<Municipio> Municipios { get; set; } = [];
    [Parameter] public EventCallback<Faturamento> OnSaved { get; set; }
    [Parameter] public EventCallback OnCancel { get; set; }
    [Inject] private IFaturamentoRestClient Client { get; set; } = null!;

    private sealed class ManualEntryModel
    {
        [Required] public string MunicipioId { get; set; } = string.Empty;
        [Required] public string Competencia { get; set; } = string.Empty;
        public decimal KwhFaturado { get; set; }
    }

    private ManualEntryModel _editModel = new();
    private bool _submitting;
    private string? _error;

    protected override void OnParametersSet()
    {
        // Reset whenever the dialog is (re)opened, so a previous attempt's input/error
        // doesn't linger the next time it's shown — same convention as InviteUserDialog.
        if (Visible)
        {
            _editModel = new ManualEntryModel();
            _error = null;
        }
    }

    private async Task HandleSubmitAsync()
    {
        if (!Guid.TryParse(_editModel.MunicipioId, out var municipioId))
        {
            _error = L["admin.ipBilling.municipios.selectOrganization"];
            return;
        }

        _submitting = true;
        _error = null;
        StateHasChanged();

        try
        {
            var result = await Client.ManualEntryAsync(municipioId, _editModel.Competencia, _editModel.KwhFaturado, performedBy: null);
            await OnSaved.InvokeAsync(result);
        }
        catch (FaturamentoAlreadyPostedException)
        {
            _error = L.GetString("admin.ipBilling.faturamento.alreadyPosted", _editModel.Competencia);
        }
        catch (Exception ex)
        {
            _error = L.GetString("admin.ipBilling.faturamento.manualEntryError", ex.Message);
        }
        finally
        {
            _submitting = false;
        }
    }

    private Task Cancel() => OnCancel.InvokeAsync();
}
