using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace GeoAssets.Shared.Components.Admin;

public partial class UserDetail
{
    [Parameter] public AppUser? User { get; set; }
    [Parameter] public EventCallback OnSaved { get; set; }
    [Parameter] public EventCallback OnCancel { get; set; }

    private string _organizationIdText = string.Empty;
    private string? _error;
    private Guid? _loadedUserId;

    private bool _roleSyncEnabled;
    private List<AppRole> _allRoles = [];
    private HashSet<string> _assignedRoleNames = [];
    private Guid? _togglingRoleId;

    protected override async Task OnParametersSetAsync()
    {
        if (User is null || User.Id == _loadedUserId) return;
        _loadedUserId = User.Id;
        _organizationIdText = User.OrganizationId?.ToString() ?? string.Empty;
        _error = null;

        var statusProvider = ServiceProvider.GetService<IRoleSyncStatusProvider>();
        _roleSyncEnabled = statusProvider is not null && await statusProvider.IsEnabledAsync();

        if (_roleSyncEnabled)
        {
            var roleSync = ServiceProvider.GetRequiredService<IRoleAssignmentProvider>();
            _allRoles = [.. await RoleRepository.GetAllAsync()];
            _assignedRoleNames = [.. await roleSync.GetAssignedRoleNamesAsync(User.ExternalObjectId)];
        }
    }

    private async Task HandleSave()
    {
        if (User is null) return;

        Guid? organizationId = null;
        if (!string.IsNullOrWhiteSpace(_organizationIdText))
        {
            if (!Guid.TryParse(_organizationIdText, out var parsed))
            {
                _error = L["admin.users.invalidOrganizationId"];
                return;
            }
            organizationId = parsed;
        }

        User.OrganizationId = organizationId;
        _error = null;

        await Repository.UpdateAsync(User);
        await Repository.SaveChangesAsync();

        await OnSaved.InvokeAsync();
    }

    private async Task Cancel() => await OnCancel.InvokeAsync();

    private async Task ToggleRoleAsync(AppRole role, bool isChecked)
    {
        if (User is null) return;
        var roleSync = ServiceProvider.GetService<IRoleAssignmentProvider>();
        if (roleSync is null) return;

        _togglingRoleId = role.Id;
        StateHasChanged();

        try
        {
            if (isChecked)
                await roleSync.AssignRoleAsync(User.ExternalObjectId, role.Name);
            else
                await roleSync.RevokeRoleAsync(User.ExternalObjectId, role.Name);

            if (isChecked) _assignedRoleNames.Add(role.Name);
            else _assignedRoleNames.Remove(role.Name);
        }
        finally
        {
            _togglingRoleId = null;
        }
    }
}
