using System.Globalization;
using System.Net;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;
using GeoAssets.Identity.Authorization.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace GeoAssets.Shared.Components.Admin;

/// <summary>
/// Issues a grant from <see cref="OrganizationId"/> to a contractor organization, or edits the rules of one it issued
/// (<see cref="Grant"/>) (XD01-411): the grantee, resource type and scope (the grant's key, fixed once issued), the
/// allowed actions, the order types (service-order grants only), whether the grant is usable only through a role held on
/// this organization, the required role and the expiry. The server decides who may and what is valid; its refusal stays in
/// the dialog, explained by its <see cref="IssuedGrantProblems"/> code.
/// </summary>
public partial class IssuedGrantDialog
{
    /// <summary>The resource types a grant can be restricted to (an empty selection means every kind).</summary>
    internal static readonly string[] ResourceTypes = [OrganizationGrantUpsertRequest.ServiceOrderResourceType, "GeoFeature"];

    private const string DateFormat = "yyyy-MM-dd";

    [Inject] private IServiceProvider ServiceProvider { get; set; } = null!;

    [Parameter] public bool Visible { get; set; }
    [Parameter] public Guid OrganizationId { get; set; }

    /// <summary>The grant to edit; null to issue a new one.</summary>
    [Parameter] public OrganizationGrantDto? Grant { get; set; }

    [Parameter] public EventCallback OnSaved { get; set; }
    [Parameter] public EventCallback OnCancel { get; set; }

    private bool _wasVisible;
    private bool _loading;
    private bool _saving;
    private List<Organization> _grantees = [];
    private List<AppPermission> _permissions = [];
    private List<AppRole> _roles = [];
    private Dictionary<Guid, string> _organizationNames = [];

    private Guid? _granteeId;
    private string _resourceType = string.Empty;
    private string? _scopeId;
    private HashSet<string> _actions = new(StringComparer.Ordinal);
    private string? _orderTypes;
    private bool _requiresScopedRole;
    private string _requiredRole = string.Empty;
    private string? _expiresOn;
    private string? _originalExpiresOn;
    private string? _error;

    private bool IsEdit => Grant is not null;

    private bool IsServiceOrder =>
        string.Equals(_resourceType, OrganizationGrantUpsertRequest.ServiceOrderResourceType, StringComparison.Ordinal);

    private bool CanSave => (IsEdit || _granteeId is not null) && _actions.Count > 0;

    private string GranteeName => Grant is { } grant && _organizationNames.TryGetValue(grant.GranteeOrganizationId, out var name)
        ? name
        : Grant?.GranteeOrganizationId.ToString() ?? string.Empty;

    protected override async Task OnParametersSetAsync()
    {
        // Reset and reload whenever the dialog is (re)opened, so a previous attempt's values or error don't linger.
        var opening = Visible && !_wasVisible;
        _wasVisible = Visible;
        if (!opening) return;

        ResetFields();
        await LoadAsync();
    }

    private void ResetFields()
    {
        _error = null;
        _granteeId = Grant?.GranteeOrganizationId;
        _resourceType = Grant?.ResourceType ?? string.Empty;
        _scopeId = Grant?.ScopeId;
        _actions = new HashSet<string>(Grant?.AllowedActions ?? [], StringComparer.Ordinal);
        _orderTypes = Grant is null ? null : string.Join(", ", Grant.OrderTypeIds);
        _requiresScopedRole = Grant?.RequiresScopedRole ?? false;
        _requiredRole = Grant?.RequiredRole ?? string.Empty;
        _expiresOn = Grant?.ExpiresAt is { } expiresAt
            ? expiresAt.ToLocalTime().ToString(DateFormat, CultureInfo.InvariantCulture)
            : null;
        _originalExpiresOn = _expiresOn;
    }

    private async Task LoadAsync()
    {
        _loading = true;
        try
        {
            var organizations = ServiceProvider.GetService<IOrganizationRepository>() is { } organizationRepository
                ? await organizationRepository.GetAllAsync()
                : [];
            _organizationNames = organizations.ToDictionary(o => o.Id, o => o.Name);
            _grantees = [.. organizations
                .Where(o => o.Id != OrganizationId)
                .OrderBy(o => o.Name, StringComparer.CurrentCultureIgnoreCase)];

            var permissions = ServiceProvider.GetService<IPermissionRepository>() is { } permissionRepository
                ? await permissionRepository.GetAllAsync()
                : [];
            // An action the grant already lists stays offered even when it is no longer a known permission, so editing
            // the grant shows what it holds.
            var unknown = _actions
                .Where(a => !permissions.Any(p => string.Equals(p.Code, a, StringComparison.Ordinal)))
                .Select(a => new AppPermission { Code = a, Resource = string.Empty, Action = string.Empty, Description = string.Empty });
            _permissions = [.. permissions.Concat(unknown).OrderBy(p => p.Code, StringComparer.Ordinal)];

            _roles = ServiceProvider.GetService<IRoleRepository>() is { } roleRepository
                ? [.. (await roleRepository.GetAllAsync()).OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)]
                : [];
        }
        catch (Exception ex)
        {
            _error = MessageFor(ex);
        }
        finally
        {
            _loading = false;
        }
    }

    private void OnGranteeChanged(ChangeEventArgs e) =>
        _granteeId = Guid.TryParse(e.Value as string, out var id) ? id : null;

    private void OnResourceTypeChanged(ChangeEventArgs e) => _resourceType = e.Value as string ?? string.Empty;

    private void ToggleAction(string code, ChangeEventArgs e)
    {
        if (e.Value is true) _actions.Add(code);
        else _actions.Remove(code);
    }

    private async Task SaveAsync()
    {
        var client = ServiceProvider.GetService<IScopedRoleAdministrationClient>();
        if (client is null || !CanSave) return;

        _saving = true;
        _error = null;
        try
        {
            var rules = BuildRules();
            if (Grant is { } grant)
            {
                await client.UpdateGrantAsync(OrganizationId, grant.Id, rules);
            }
            else
            {
                await client.IssueGrantAsync(OrganizationId, new IssuedGrantCreateRequest(
                    _granteeId!.Value,
                    _resourceType.Length == 0 ? null : _resourceType,
                    string.IsNullOrWhiteSpace(_scopeId) ? null : _scopeId.Trim(),
                    rules));
            }

            await OnSaved.InvokeAsync();
        }
        catch (Exception ex)
        {
            _error = MessageFor(ex);
        }
        finally
        {
            _saving = false;
        }
    }

    private IssuedGrantRules BuildRules()
    {
        IReadOnlyList<string> orderTypes = IsServiceOrder && !string.IsNullOrWhiteSpace(_orderTypes)
            ? [.. _orderTypes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)]
            : [];

        return new IssuedGrantRules(
            [.. _actions.Order(StringComparer.Ordinal)],
            _requiredRole.Length == 0 ? null : _requiredRole,
            _requiresScopedRole,
            orderTypes,
            ExpiresAt());
    }

    /// <summary>
    /// The chosen expiry: the end of the chosen day in the viewer's time zone, as UTC — so the list shows the same date —
    /// or the grant's own expiry when the date was left as it was.
    /// </summary>
    private DateTime? ExpiresAt()
    {
        if (Grant is { } grant && string.Equals(_expiresOn, _originalExpiresOn, StringComparison.Ordinal)) return grant.ExpiresAt;
        if (string.IsNullOrWhiteSpace(_expiresOn)
            || !DateOnly.TryParseExact(_expiresOn, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            return null;

        return day.ToDateTime(new TimeOnly(23, 59, 59), DateTimeKind.Local).ToUniversalTime();
    }

    private string MessageFor(Exception ex) => ex is ScopedRoleAdministrationException refusal
        ? refusal.Code switch
        {
            IssuedGrantProblems.Invalid      => L["admin.contractorAccess.grantForm.error.invalid"],
            IssuedGrantProblems.Exists       => L["admin.contractorAccess.grantForm.error.exists"],
            IssuedGrantProblems.ServiceOwned => L["admin.contractorAccess.grantForm.error.serviceOwned"],
            IssuedGrantProblems.Conflict     => L["admin.contractorAccess.grantForm.error.conflict"],
            IssuedGrantProblems.NotFound     => L["admin.contractorAccess.grantForm.error.notFound"],
            _ when refusal.StatusCode == (int)HttpStatusCode.Forbidden => L["admin.contractorAccess.grantForm.error.forbidden"],
            _                                => L["admin.contractorAccess.grantForm.error.generic"],
        }
        : L["admin.contractorAccess.grantForm.error.generic"];

    private Task Cancel() => OnCancel.InvokeAsync();
}
