using System.Globalization;
using System.Net;
using GeoAssets.Identity.Authorization.Repositories;
using GeoAssets.Identity.Authorization.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GeoAssets.Shared.Components.Admin;

/// <summary>
/// "Contractor access" (XD01-327) on the organization admin surface: the grants the organization has issued (read-only,
/// with <c>RequiresScopedRole</c> and <c>OrderTypeIds</c>), the roles users of those contracted organizations hold on it,
/// a revoke per role, and <see cref="AssignScopedRoleDialog"/> to give one. Rendered only where
/// <see cref="IScopedRoleAdministrationClient"/> resolves (the REST-backed identity stack). The server decides who may
/// administer the organization; its 403 shows <c>admin.contractorAccess.forbidden</c> instead of the panel.
/// </summary>
public partial class ContractorAccessPanel
{
    [Inject] private ILogger<ContractorAccessPanel> Logger { get; set; } = null!;

    private enum PanelState { Loading, Loaded, Forbidden, Failed }

    [Inject] private IServiceProvider ServiceProvider { get; set; } = null!;

    [Parameter] public Guid OrganizationId { get; set; }

    private static CultureInfo Culture => CultureInfo.CurrentCulture;

    private IScopedRoleAdministrationClient? _client;
    private PanelState _state = PanelState.Loading;
    private Guid? _loadedOrganizationId;
    private List<OrganizationGrantDto> _grants = [];
    private List<ScopedRoleAssignmentDto> _assignments = [];
    private Dictionary<Guid, string> _organizationNames = [];
    private bool _showAssignDialog;
    private bool _showRevokeConfirm;
    private ScopedRoleAssignmentDto? _toRevoke;
    private string? _actionError;

    protected override async Task OnParametersSetAsync()
    {
        _client ??= ServiceProvider.GetService<IScopedRoleAdministrationClient>();
        if (_client is null || OrganizationId == _loadedOrganizationId) return;

        _loadedOrganizationId = OrganizationId;
        await RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        if (_client is null) return;

        _state = PanelState.Loading;
        _actionError = null;
        try
        {
            _grants = [.. await _client.GetIssuedGrantsAsync(OrganizationId)];
            _assignments = [.. await _client.GetAssignmentsAsync(OrganizationId)];
            _organizationNames = await LoadOrganizationNamesAsync();
            _state = PanelState.Loaded;
        }
        catch (ScopedRoleAdministrationException ex) when (ex.StatusCode == (int)HttpStatusCode.Forbidden)
        {
            _state = PanelState.Forbidden;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to load contractor access");
            _state = PanelState.Failed;
        }
    }

    private async Task<Dictionary<Guid, string>> LoadOrganizationNamesAsync()
    {
        var organizations = ServiceProvider.GetService<IOrganizationRepository>();
        if (organizations is null) return [];

        var all = await organizations.GetAllAsync();
        return all.ToDictionary(o => o.Id, o => o.Name);
    }

    private string OrganizationName(Guid organizationId) =>
        _organizationNames.TryGetValue(organizationId, out var name) ? name : organizationId.ToString();

    private void OpenAssignDialog()
    {
        _actionError = null;
        _showAssignDialog = true;
    }

    private Task OnAssignedAsync()
    {
        _showAssignDialog = false;
        return RefreshAsync();
    }

    private void RequestRevoke(ScopedRoleAssignmentDto assignment)
    {
        _toRevoke = assignment;
        _showRevokeConfirm = true;
    }

    private async Task ConfirmRevokeAsync()
    {
        _showRevokeConfirm = false;
        if (_client is null || _toRevoke is null) return;

        try
        {
            await _client.RevokeAsync(OrganizationId, _toRevoke.Id);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to revoke contractor access {AssignmentId}", _toRevoke.Id);
            _actionError = L["admin.contractorAccess.revokeError"];
        }
        finally
        {
            _toRevoke = null;
        }
    }
}
