using GeoAssets.IpBilling.Models;
using GeoAssets.Shared.Components.IpBilling;

namespace GeoAssets.Shared.Pages.IpBilling;

public partial class Municipios
{
    private MunicipioList? _list;
    private Guid? _selectedMunicipioId;
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
            await Console.Error.WriteLineAsync($"[AdminIpBillingMunicipios] Failed to resolve authorization: {ex.Message}");
            _authorized = false;
        }
    }

    private void OnMunicipioSelected(Municipio municipio)
    {
        _selectedMunicipioId = municipio.Id;
        _showCreateForm = false;
        StateHasChanged();
    }

    private void OnCreateRequested()
    {
        _selectedMunicipioId = null;
        _showCreateForm = true;
        StateHasChanged();
    }

    private async Task OnMunicipioSaved(Guid municipioId)
    {
        _showCreateForm = false;
        _selectedMunicipioId = municipioId;
        if (_list is not null) await _list.RefreshAsync();
    }

    private async Task OnMunicipioDeleted()
    {
        _selectedMunicipioId = null;
        if (_list is not null) await _list.RefreshAsync();
    }
}
