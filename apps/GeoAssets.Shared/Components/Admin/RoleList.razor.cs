using GeoAssets.Identity.Authorization.Models;
using Microsoft.AspNetCore.Components;

namespace GeoAssets.Shared.Components.Admin;

public partial class RoleList
{
    [Parameter] public Guid? SelectedRoleId { get; set; }
    [Parameter] public EventCallback<AppRole> OnRoleSelected { get; set; }
    [Parameter] public EventCallback OnCreateRequested { get; set; }

    private List<AppRole> _roles = [];
    private bool _loading = true;

    protected override async Task OnInitializedAsync() => await RefreshAsync();

    public async Task RefreshAsync()
    {
        _loading = true;
        StateHasChanged();
        _roles = [.. await Repository.GetAllAsync()];
        _loading = false;
        StateHasChanged();
    }

    private async Task SelectRole(AppRole role) => await OnRoleSelected.InvokeAsync(role);
}
