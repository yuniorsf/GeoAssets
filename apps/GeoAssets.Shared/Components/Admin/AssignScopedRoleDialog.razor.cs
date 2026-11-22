using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;
using GeoAssets.Identity.Authorization.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace GeoAssets.Shared.Components.Admin;

/// <summary>
/// Gives a user of a contracted organization a role on <see cref="OrganizationId"/> (XD01-327): pick one of the
/// organization's contracted users (<c>contracted-users</c>), pick a role, assign. The server's refusal stays in the dialog,
/// explained by its <see cref="ScopedRoleAssignmentProblems"/> code.
/// </summary>
public partial class AssignScopedRoleDialog
{
    [Inject] private IServiceProvider ServiceProvider { get; set; } = null!;

    [Parameter] public bool Visible { get; set; }
    [Parameter] public Guid OrganizationId { get; set; }
    [Parameter] public EventCallback OnAssigned { get; set; }
    [Parameter] public EventCallback OnCancel { get; set; }

    private bool _wasVisible;
    private bool _loading;
    private bool _assigning;
    private List<ContractedUserDto> _users = [];
    private List<AppRole> _roles = [];
    private Guid? _userId;
    private Guid? _roleId;
    private string? _error;

    protected override async Task OnParametersSetAsync()
    {
        // Reload whenever the dialog is (re)opened, so the list reflects grants issued or revoked meanwhile and a previous
        // attempt's choice or error doesn't linger.
        var opening = Visible && !_wasVisible;
        _wasVisible = Visible;
        if (!opening) return;

        _userId = null;
        _roleId = null;
        _error = null;
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        var client = ServiceProvider.GetService<IScopedRoleAdministrationClient>();
        var roles = ServiceProvider.GetService<IRoleRepository>();
        if (client is null || roles is null) return;

        _loading = true;
        try
        {
            _users = [.. await client.GetContractedUsersAsync(OrganizationId)];
            _roles = [.. (await roles.GetAllAsync()).OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)];
        }
        catch (Exception ex)
        {
            _users = [];
            _error = MessageFor(ex);
        }
        finally
        {
            _loading = false;
        }
    }

    private void OnUserChanged(ChangeEventArgs e) => _userId = ParseId(e);

    private void OnRoleChanged(ChangeEventArgs e) => _roleId = ParseId(e);

    private static Guid? ParseId(ChangeEventArgs e) =>
        Guid.TryParse(e.Value as string, out var id) ? id : null;

    private async Task AssignAsync()
    {
        var client = ServiceProvider.GetService<IScopedRoleAdministrationClient>();
        if (client is null || _userId is not { } userId || _roleId is not { } roleId) return;

        _assigning = true;
        _error = null;
        try
        {
            await client.AssignAsync(OrganizationId, userId, roleId);
            await OnAssigned.InvokeAsync();
        }
        catch (Exception ex)
        {
            _error = MessageFor(ex);
        }
        finally
        {
            _assigning = false;
        }
    }

    private string MessageFor(Exception ex) => ex is ScopedRoleAdministrationException refusal
        ? refusal.Code switch
        {
            ScopedRoleAssignmentProblems.NotContracted  => L["admin.contractorAccess.error.notContracted"],
            ScopedRoleAssignmentProblems.SelfAssignment => L["admin.contractorAccess.error.selfAssignment"],
            ScopedRoleAssignmentProblems.Invalid        => L["admin.contractorAccess.error.invalid"],
            ScopedRoleAssignmentProblems.NotFound       => L["admin.contractorAccess.error.notFound"],
            ScopedRoleAssignmentProblems.Forbidden      => L["admin.contractorAccess.forbidden"],
            _                                           => L["admin.contractorAccess.error.generic"],
        }
        : L["admin.contractorAccess.error.generic"];

    private Task Cancel() => OnCancel.InvokeAsync();
}
