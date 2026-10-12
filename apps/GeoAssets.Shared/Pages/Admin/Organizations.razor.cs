using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Shared.Components.Admin;

namespace GeoAssets.Shared.Pages.Admin;

public partial class Organizations
{
    private OrganizationList? _list;
    private Guid? _selectedOrganizationId;
    private bool _showCreateForm;
    private bool? _authorized;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            _authorized = await AuthService.HasPermissionAsync("organizations:read");
        }
        catch (Exception ex)
        {
            // Fail closed: if the authorization check itself can't be answered (e.g. the
            // identity backend is unreachable), don't show admin functionality.
            Console.Error.WriteLine($"[AdminOrganizations] Failed to resolve authorization: {ex.Message}");
            _authorized = false;
        }
    }

    private void OnOrganizationSelected(Organization organization)
    {
        _selectedOrganizationId = organization.Id;
        _showCreateForm = false;
        StateHasChanged();
    }

    private void OnCreateRequested()
    {
        _selectedOrganizationId = null;
        _showCreateForm = true;
        StateHasChanged();
    }

    private async Task OnOrganizationSaved(Guid organizationId)
    {
        _showCreateForm = false;
        _selectedOrganizationId = organizationId;
        if (_list is not null) await _list.RefreshAsync();
    }
}
