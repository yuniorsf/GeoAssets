using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace GeoAssets.Shared.Pages.Admin;

public partial class Permissions
{
    [Inject] private ILogger<Permissions> Logger { get; set; } = null!;

    private bool? _authorized;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            _authorized = await AuthService.HasPermissionAsync("permissions:read");
        }
        catch (Exception ex)
        {
            // Fail closed: if the authorization check itself can't be answered (e.g. the
            // identity backend is unreachable), don't show admin functionality.
            Logger.LogWarning(ex, "Failed to resolve authorization");
            _authorized = false;
        }
    }
}
