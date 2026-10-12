using GeoAssets.Identity.Authorization.Models;
using Microsoft.AspNetCore.Components;

namespace GeoAssets.Shared.Components.Admin;

public partial class OrganizationList
{
    [Parameter] public Guid? SelectedOrganizationId { get; set; }
    [Parameter] public EventCallback<Organization> OnOrganizationSelected { get; set; }
    [Parameter] public EventCallback OnCreateRequested { get; set; }

    private List<Organization> _organizations = [];
    private bool _loading = true;

    protected override async Task OnInitializedAsync() => await RefreshAsync();

    public async Task RefreshAsync()
    {
        _loading = true;
        StateHasChanged();
        _organizations = [.. await Repository.GetAllAsync()];
        _loading = false;
        StateHasChanged();
    }

    private async Task SelectOrganization(Organization organization) => await OnOrganizationSelected.InvokeAsync(organization);
}
