using GeoAssets.IpBilling.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace GeoAssets.IpBilling.UI.Pages;

public partial class LampReference
{
    [Inject] private ILogger<LampReference> Logger { get; set; } = null!;

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
            AuthorizationLog.AuthorizationCheckFailed(Logger, ex);
            _authorized = false;
        }
    }

    private void OnLampTypeSelected(LampType lampType)
    {
        _selectedLampType = lampType;
        StateHasChanged();
    }
}
