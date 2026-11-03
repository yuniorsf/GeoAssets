using System.ComponentModel.DataAnnotations;
using GeoAssets.IpBilling.Fiscalizacao;
using GeoAssets.IpBilling.Models;
using GeoAssets.Shared.Components.IpBilling;
using GeoAssets.Workflow.Orders;
using Microsoft.AspNetCore.Components;

namespace GeoAssets.Shared.Pages.IpBilling;

public partial class Fiscalizacoes
{
    [Inject] private IFiscalizacaoRestClient Client { get; set; } = null!;
    [Inject] private IServiceOrderRepository OrderRepository { get; set; } = null!;

    private sealed class CreateFiscalizacaoModel
    {
        [Required] public string MunicipioIbgeCode { get; set; } = string.Empty;
        [Required] public string Competencia { get; set; } = string.Empty;
    }

    private FiscalizacaoList? _list;
    private IServiceOrder? _selectedOrder;
    private bool _showCreateForm;
    private bool? _authorized;
    private List<Municipio> _municipios = [];
    private CreateFiscalizacaoModel _createModel = new();
    private string? _createError;

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
            await Console.Error.WriteLineAsync($"[AdminIpBillingFiscalizacoes] Failed to resolve authorization: {ex.Message}");
            _authorized = false;
            return;
        }

        if (_authorized == true)
        {
            _municipios = [.. await MunicipioRepository.GetAllAsync()];
        }
    }

    private void OnOrderSelected(IServiceOrder order)
    {
        _selectedOrder = order;
        _showCreateForm = false;
        StateHasChanged();
    }

    private void OnCreateRequested()
    {
        _selectedOrder = null;
        _createModel = new CreateFiscalizacaoModel();
        _createError = null;
        _showCreateForm = true;
        StateHasChanged();
    }

    private async Task HandleCreateAsync()
    {
        _createError = null;

        try
        {
            var order = await Client.CreateAsync(_createModel.MunicipioIbgeCode, _createModel.Competencia, createdBy: null);
            _showCreateForm = false;
            _selectedOrder = order;
            if (_list is not null) await _list.RefreshAsync();
        }
        catch (Exception ex)
        {
            _createError = L.GetString("admin.ipBilling.fiscalizacoes.createError", ex.Message);
        }
    }

    private async Task ReloadSelectedAsync()
    {
        if (_selectedOrder is null) return;
        _selectedOrder = await OrderRepository.GetByIdAsync(_selectedOrder.Id) ?? _selectedOrder;
        StateHasChanged();
    }
}
