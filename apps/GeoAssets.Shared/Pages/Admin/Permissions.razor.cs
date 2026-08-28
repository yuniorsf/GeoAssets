namespace GeoAssets.Shared.Pages.Admin;

public partial class Permissions
{
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
            Console.Error.WriteLine($"[AdminPermissions] Failed to resolve authorization: {ex.Message}");
            _authorized = false;
        }
    }
}
