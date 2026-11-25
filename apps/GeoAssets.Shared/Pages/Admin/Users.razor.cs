using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Shared.Components.Admin;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace GeoAssets.Shared.Pages.Admin;

public partial class Users
{
    [Inject] private ILogger<Users> Logger { get; set; } = null!;

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
            Logger.LogWarning(ex, "Failed to resolve authorization");
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
