using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Shared.Components.Admin;

namespace GeoAssets.Shared.Pages.Admin;

public partial class Users
{
    private UserList? _list;
    private AppUser? _selected;
    private bool? _authorized;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            _authorized = await AuthService.HasPermissionAsync("users:read");
        }
        catch (Exception ex)
        {
            // Fail closed: if the authorization check itself can't be answered (e.g. the
            // identity backend is unreachable), don't show admin functionality.
            Console.Error.WriteLine($"[AdminUsers] Failed to resolve authorization: {ex.Message}");
            _authorized = false;
        }
    }

    private void OnUserSelected(AppUser user)
    {
        _selected = user;
        StateHasChanged();
    }

    private async Task OnUserSaved()
    {
        _selected = null;
        if (_list is not null) await _list.RefreshAsync();
    }
}
