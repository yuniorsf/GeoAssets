using GeoAssets.Identity.Authorization.Models;
using Microsoft.AspNetCore.Components;

namespace GeoAssets.Shared.Components.Admin;

public partial class UserList
{
    [Parameter] public Guid? SelectedUserId { get; set; }
    [Parameter] public EventCallback<AppUser> OnUserSelected { get; set; }

    private List<AppUser> _users = [];
    private bool _loading = true;

    protected override async Task OnInitializedAsync() => await RefreshAsync();

    public async Task RefreshAsync()
    {
        _loading = true;
        StateHasChanged();
        _users = [.. await Repository.GetAllAsync()];
        _loading = false;
        StateHasChanged();
    }

    private async Task SelectUser(AppUser user) => await OnUserSelected.InvokeAsync(user);
}
