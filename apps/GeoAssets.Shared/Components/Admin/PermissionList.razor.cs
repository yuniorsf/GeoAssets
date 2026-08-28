using GeoAssets.Identity.Authorization.Models;

namespace GeoAssets.Shared.Components.Admin;

public partial class PermissionList
{
    private List<AppPermission> _permissions = [];
    private bool _loading = true;

    protected override async Task OnInitializedAsync()
    {
        _permissions = [.. await Repository.GetAllAsync()];
        _loading = false;
    }
}
