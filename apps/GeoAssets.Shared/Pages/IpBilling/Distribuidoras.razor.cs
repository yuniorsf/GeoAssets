using GeoAssets.IpBilling.Models;
using GeoAssets.Shared.Components.IpBilling;

namespace GeoAssets.Shared.Pages.IpBilling;

/// <summary>
/// Admin page for Distribuidoras and each one's lamp wattage chart (XD01-301). Same shape and
/// <c>ip-billing:reference-manage</c> gate as <see cref="Municipios"/>.
/// </summary>
public partial class Distribuidoras
{
    private DistribuidoraList? _list;
    private Guid? _selectedDistribuidoraId;
    private bool _showCreateForm;
    private bool? _authorized;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            _authorized = await AuthService.HasPermissionAsync("ip-billing:reference-manage");
        }
        catch (Exception ex)
        {
            // Fail closed: if the authorization check itself can't be answered (e.g. the
            // identity backend is unreachable), don't show admin functionality.
            await Console.Error.WriteLineAsync($"[AdminIpBillingDistribuidoras] Failed to resolve authorization: {ex.Message}");
            _authorized = false;
        }
    }

    private void OnDistribuidoraSelected(Distribuidora distribuidora)
    {
        _selectedDistribuidoraId = distribuidora.Id;
        _showCreateForm = false;
        StateHasChanged();
    }

    private void OnCreateRequested()
    {
        _selectedDistribuidoraId = null;
        _showCreateForm = true;
        StateHasChanged();
    }

    private async Task OnDistribuidoraSavedAsync(Guid distribuidoraId)
    {
        _showCreateForm = false;
        _selectedDistribuidoraId = distribuidoraId;
        if (_list is not null) await _list.RefreshAsync();
    }

    private async Task OnDistribuidoraDeletedAsync()
    {
        _selectedDistribuidoraId = null;
        if (_list is not null) await _list.RefreshAsync();
    }
}
