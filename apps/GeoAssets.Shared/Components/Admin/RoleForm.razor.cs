using System.ComponentModel.DataAnnotations;
using System.Net;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace GeoAssets.Shared.Components.Admin;

public partial class RoleForm
{
    [Parameter] public Guid? RoleId { get; set; }
    [Parameter] public bool IsNew { get; set; }
    [Parameter] public EventCallback<Guid> OnSaved { get; set; }
    [Parameter] public EventCallback OnCancel { get; set; }
    [Parameter] public EventCallback OnDeleted { get; set; }

    private sealed class RoleEditModel
    {
        [Required] public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }

    private RoleEditModel _editModel = new();
    private AppRole? _role;
    private List<AppPermission> _allPermissions = [];
    private HashSet<Guid> _grantedPermissionIds = [];
    private Guid? _togglingPermissionId;
    private bool _loading = true;
    private bool _showDeleteConfirm;
    private string? _error;
    private Guid? _loadedRoleId;

    private bool _roleSyncEnabled;
    private bool _registeringInEntra;
    private string? _registerSuccessMessage;

    protected override async Task OnInitializedAsync()
    {
        // Role-sync status is global (not per-role), so this only needs to run once per
        // component instance, not on every RoleId change (unlike OnParametersSetAsync below).
        var statusProvider = ServiceProvider.GetService<IRoleSyncStatusProvider>();
        _roleSyncEnabled = statusProvider is not null && await statusProvider.IsEnabledAsync();
    }

    protected override async Task OnParametersSetAsync()
    {
        if (IsNew)
        {
            _loadedRoleId = null;
            _role = null;
            _editModel = new RoleEditModel();
            _grantedPermissionIds = [];
            _loading = false;
            return;
        }

        if (RoleId is null || RoleId == _loadedRoleId) return;

        _loading = true;
        _registerSuccessMessage = null;
        StateHasChanged();

        _loadedRoleId = RoleId;
        _allPermissions = [.. await PermissionRepository.GetAllAsync()];
        _role = await RoleRepository.GetByIdAsync(RoleId.Value);
        if (_role is not null)
        {
            _editModel = new RoleEditModel { Name = _role.Name, Description = _role.Description };
            _grantedPermissionIds = [.. _role.RolePermissions.Select(rp => rp.PermissionId)];
        }
        _loading = false;
    }

    private async Task HandleSave()
    {
        _error = null;

        if (IsNew)
        {
            var role = new AppRole
            {
                Id          = Guid.NewGuid(),
                Name        = _editModel.Name,
                Description = _editModel.Description,
                IsBuiltIn   = false,
            };
            await RoleRepository.AddAsync(role);
            await RoleRepository.SaveChangesAsync();
            await OnSaved.InvokeAsync(role.Id);
        }
        else if (_role is not null)
        {
            _role.Name        = _editModel.Name;
            _role.Description = _editModel.Description;
            await RoleRepository.UpdateAsync(_role);
            await RoleRepository.SaveChangesAsync();
            await OnSaved.InvokeAsync(_role.Id);
        }
    }

    private async Task TogglePermissionAsync(AppPermission permission, bool isChecked)
    {
        if (_role is null) return;

        _togglingPermissionId = permission.Id;
        StateHasChanged();

        try
        {
            if (isChecked)
                await RoleRepository.GrantPermissionAsync(_role.Id, permission.Id);
            else
                await RoleRepository.RevokePermissionAsync(_role.Id, permission.Id);
            await RoleRepository.SaveChangesAsync();

            if (isChecked)
                _grantedPermissionIds.Add(permission.Id);
            else
                _grantedPermissionIds.Remove(permission.Id);
        }
        finally
        {
            _togglingPermissionId = null;
        }
    }

    private async Task RegisterInEntraAsync()
    {
        if (_role is null) return;
        var roleSync = ServiceProvider.GetService<IRoleAssignmentProvider>();
        if (roleSync is null) return;

        _registeringInEntra = true;
        _registerSuccessMessage = null;
        _error = null;
        StateHasChanged();

        try
        {
            await roleSync.RegisterRoleAsync(_role);
            _registerSuccessMessage = L["admin.roles.registerSuccess"];
        }
        catch (Exception)
        {
            // The Graph credential/network is genuinely allowed to fail here (expired secret,
            // throttling) — surface it without taking down the rest of the form, matching the
            // ConfirmDelete 409-guard precedent above.
            _error = L["admin.roles.registerError"];
        }
        finally
        {
            _registeringInEntra = false;
        }
    }

    private void RequestDelete() => _showDeleteConfirm = true;

    private async Task ConfirmDelete()
    {
        if (_role is null) return;
        _showDeleteConfirm = false;

        try
        {
            await RoleRepository.DeleteAsync(_role.Id);
            await RoleRepository.SaveChangesAsync();
            await OnDeleted.InvokeAsync();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            // Defensive: the delete button is already hidden for built-in roles, but the
            // server is the source of truth (409 per XD01-56) — surface it cleanly instead
            // of an unhandled exception if this is ever reached (e.g. a stale client state).
            _error = L["admin.roles.builtInDeleteError"];
        }
    }

    private async Task Cancel() => await OnCancel.InvokeAsync();
}
