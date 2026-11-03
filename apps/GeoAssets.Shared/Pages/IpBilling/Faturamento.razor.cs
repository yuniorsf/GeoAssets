using GeoAssets.IpBilling.FaturamentoLedger;
using GeoAssets.IpBilling.Models;
using Microsoft.AspNetCore.Components;

namespace GeoAssets.Shared.Pages.IpBilling;

public partial class Faturamento
{
    [Inject] private IFaturamentoRestClient Client { get; set; } = null!;

    private bool? _authorized;
    private List<Municipio> _municipios = [];
    private readonly Dictionary<Guid, string> _municipioLabels = [];
    private IReadOnlyList<GeoAssets.IpBilling.Models.Faturamento> _rows = [];
    private string _filterMunicipioId = string.Empty;
    private string _filterCompetencia = string.Empty;
    private string? _loadError;
    private bool _showManualEntry;
    private bool _showRecarga;
    private GeoAssets.IpBilling.Models.Faturamento? _recargaTarget;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            _authorized = await AuthService.HasPermissionAsync("ip-billing:upload");
        }
        catch (Exception ex)
        {
            // Fail closed: if the authorization check itself can't be answered (e.g. the
            // identity backend is unreachable), don't show admin functionality.
            await Console.Error.WriteLineAsync($"[AdminIpBillingFaturamento] Failed to resolve authorization: {ex.Message}");
            _authorized = false;
            return;
        }

        if (_authorized == true)
        {
            _municipios = [.. await MunicipioRepository.GetAllAsync()];
            foreach (var municipio in _municipios)
            {
                _municipioLabels[municipio.Id] = $"{municipio.Name} ({municipio.Uf})";
            }
        }
    }

    private string MunicipioLabel(Guid municipioId) =>
        _municipioLabels.TryGetValue(municipioId, out var label) ? label : municipioId.ToString();

    private async Task LoadRowsAsync()
    {
        _loadError = null;

        if (!Guid.TryParse(_filterMunicipioId, out var municipioId))
        {
            _rows = [];
            return;
        }

        try
        {
            var competencia = string.IsNullOrWhiteSpace(_filterCompetencia) ? null : _filterCompetencia;
            _rows = await Client.ConsultaAsync(municipioId, competencia);
        }
        catch (Exception ex)
        {
            _loadError = L.GetString("admin.ipBilling.faturamento.loadError", ex.Message);
            _rows = [];
        }
    }

    private void OnRecargaRequested(GeoAssets.IpBilling.Models.Faturamento target)
    {
        _recargaTarget = target;
        _showRecarga = true;
    }

    private Task OnManualEntrySaved(GeoAssets.IpBilling.Models.Faturamento _)
    {
        _showManualEntry = false;
        return LoadRowsAsync();
    }

    private Task OnRecargaCompleted(FaturamentoBatchResult _)
    {
        _showRecarga = false;
        return LoadRowsAsync();
    }
}
