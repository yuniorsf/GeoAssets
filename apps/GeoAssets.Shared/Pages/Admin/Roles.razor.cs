using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Shared.Components.Admin;

namespace GeoAssets.Shared.Pages.Admin;

public partial class Roles
{
    private RoleList? _list;
    private Guid? _selectedRoleId;
    private bool _showCreateForm;
    private bool? _authorized;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            _authorized = await AuthService.HasPermissionAsync("roles:read");
        }
        catch (Exception ex)
        {
            // Fail closed: if the authorization check itself can't be answered (e.g. the
            // identity backend is unreachable), don't show admin functionality.
            Console.Error.WriteLine($"[AdminRoles] Failed to resolve authorization: {ex.Message}");
            _authorized = false;
        }
    }

    private void OnRoleSelected(AppRole role)
    {
        _selectedRoleId = role.Id;
        _showCreateForm = false;
        StateHasChanged();
    }

    private void OnCreateRequested()
    {
        _selectedRoleId = null;
        _showCreateForm = true;
        StateHasChanged();
    }

    private async Task OnRoleSaved(Guid roleId)
    {
        _showCreateForm = false;
        _selectedRoleId = roleId;
        if (_list is not null) await _list.RefreshAsync();
    }

    private async Task OnRoleDeleted()
    {
        _selectedRoleId = null;
        if (_list is not null) await _list.RefreshAsync();
    }
}
