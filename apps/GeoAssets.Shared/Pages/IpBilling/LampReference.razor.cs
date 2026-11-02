using GeoAssets.IpBilling.Models;

namespace GeoAssets.Shared.Pages.IpBilling;

public partial class LampReference
{
    private LampType? _selectedLampType;
    private bool? _authorized;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            _authorized = await AuthService.HasPermissionAsync("ip-billing:read");
        }
        catch (Exception ex)
        {
            // Fail closed: if the authorization check itself can't be answered (e.g. the
            // identity backend is unreachable), don't show admin functionality.
            await Console.Error.WriteLineAsync($"[AdminIpBillingLampReference] Failed to resolve authorization: {ex.Message}");
            _authorized = false;
        }
    }

    private void OnLampTypeSelected(LampType lampType)
    {
        _selectedLampType = lampType;
        StateHasChanged();
    }
}
