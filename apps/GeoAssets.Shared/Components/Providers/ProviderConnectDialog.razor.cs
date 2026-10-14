using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Identity.Authorization.Services;
using GeoAssets.Shared.Components.Projects;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GeoAssets.Shared.Components.Providers;

public partial class ProviderConnectDialog
{
    private enum ConnectTab { Provider, Project }

    /// <summary>Controls dialog visibility. For boot mode, drive with <c>!BootLoader.IsBootComplete</c>.</summary>
    [Parameter] public bool Visible { get; set; }

    /// <summary>
    /// True: dialog is the boot-time provider picker (shows Skip, routes through IBootLoader).
    /// False: dialog is the runtime "add provider" flow (shows Cancel, adds directly to pool).
    /// </summary>
    [Parameter] public bool IsBootMode { get; set; }

    /// <summary>Called when the dialog should close (Cancel pressed or connection succeeded in runtime mode).</summary>
    [Parameter] public EventCallback OnClose { get; set; }

    /// <summary>Called after a provider is successfully connected in runtime mode. Use this to render features on the map.</summary>
    [Parameter] public EventCallback<ProviderEntry> OnProviderConnected { get; set; }

    private IProviderPlugin? _selected;
    private ProviderConfig   _config    = new();
    private string           _error     = string.Empty;
    private bool             _connecting;

    // ── Project tab — lets the boot dialog open an existing Project instead of forcing a raw
    // provider connection. Only offered in boot mode, and only when Projects is actually
    // available (Web today, not MAUI — IProjectSessionService isn't registered there; resolved
    // optionally via IServiceProvider, same defensive pattern as ProviderPoolPanel.Session). ──

    private ConnectTab _activeTab = ConnectTab.Provider;
    private bool            _loadingProjects;
    private string          _projectError    = string.Empty;
    private List<Project>   _generalProjects = [];
    private List<Project>   _myForks         = [];

    private IProjectSessionService?   Session        => Services.GetService<IProjectSessionService>();
    private IProjectClient?           ProjectClient  => Services.GetService<IProjectClient>();
    private IGeoAuthorizationService? AuthService     => Services.GetService<IGeoAuthorizationService>();

    private bool ShowProjectTab => IsBootMode && Session is not null;

    protected override void OnInitialized()
    {
        // Re-render when auto-boot completes so the overlay disappears immediately
        if (IsBootMode)
            BootLoader.BootCompleted += (_, _) => InvokeAsync(StateHasChanged);

        if (Registry.All.Count > 0)
            SelectPlugin(Registry.All[0]);
    }

    protected override void OnParametersSet()
    {
        // Reset form every time the dialog is opened from outside (Visible flips false→true)
        if (Visible && _selected is null && Registry.All.Count > 0)
            SelectPlugin(Registry.All[0]);
    }

    private async Task SelectTab(ConnectTab tab)
    {
        _activeTab = tab;
        if (tab == ConnectTab.Project && _generalProjects.Count == 0 && _myForks.Count == 0 && !_loadingProjects)
            await LoadProjectsAsync();
    }

    private async Task LoadProjectsAsync()
    {
        if (ProjectClient is null || AuthService is null) return;

        _loadingProjects = true;
        _projectError    = string.Empty;
        StateHasChanged();

        try
        {
            var context = await AuthService.GetAuthorizationContextAsync();
            if (context.User.OrganizationId is not { } organizationId)
            {
                _generalProjects = [];
                _myForks         = [];
                return;
            }

            var all = await ProjectClient.GetByOrganizationAsync(organizationId);
            var (general, forks) = ProjectPanel.Categorize(all, context.User.Id);
            _generalProjects = [.. general];
            _myForks         = [.. forks];
        }
        catch (Exception ex)
        {
            _projectError = L.GetString("projects.errorOpen", ex.Message);
        }
        finally
        {
            _loadingProjects = false;
        }
    }

    private async Task OpenProjectAsync(Project project)
    {
        if (Session is null) return;

        _projectError = string.Empty;
        try
        {
            if (!await Session.RequestCloseAsync()) return;
            await Session.OpenAsync(project.Id);

            // The Project's own saved providers (if any) are already reconnected into the pool
            // by Session.OpenAsync — no provider-creation step needed here, just mark boot done.
            BootLoader.CompleteWithoutProvider();
            // BootCompleted event (subscribed above) triggers StateHasChanged → overlay disappears
        }
        catch (Exception ex)
        {
            _projectError = L.GetString("projects.errorOpen", ex.Message);
        }
    }

    private void SkipBoot() => BootLoader.CompleteWithoutProvider();

    private void SelectPlugin(IProviderPlugin plugin)
    {
        _selected = plugin;
        _config   = new ProviderConfig();
        _error    = string.Empty;
    }

    private async Task ConnectAsync()
    {
        if (_selected is null) return;

        foreach (var f in _selected.ConfigFields.Where(f => f.Required))
        {
            var key = f.Type is ProviderFieldType.File or ProviderFieldType.BinaryFile
                ? f.Key + "_content"
                : f.Key;

            if (!_config.Has(key))
            {
                _error = L.GetString("pool.fieldRequired", f.Label);
                return;
            }
        }

        _connecting = true;
        _error      = string.Empty;
        StateHasChanged();

        try
        {
            if (IsBootMode)
            {
                var entry = await BootLoader.BootWithAsync(_selected, _config);
                // BootCompleted event triggers StateHasChanged → overlay disappears
                await OnProviderConnected.InvokeAsync(entry);
            }
            else
            {
                var provider = await _selected.CreateAsync(_config, Services);
                var name     = _config.Get("name", _selected.DisplayName);
                var entry    = Pool.Add(name, provider, _selected.Id, _config.ToReconnectValues(_selected.ConfigFields));

                Logger.LogInformation("Provider connected — plugin: {Plugin}, name: {Name}, features: {Count}",
                    _selected.Id, name, provider.GetAll().Count);

                await OnProviderConnected.InvokeAsync(entry);
                await OnClose.InvokeAsync();

                // Reset for next open
                _selected = Registry.All.Count > 0 ? Registry.All[0] : null;
                _config   = new();
            }
        }
        catch (Exception ex)
        {
            _error = ex.Message;
            Logger.LogError(ex, "Provider connection failed — plugin: {Plugin}", _selected?.Id);
        }
        finally
        {
            _connecting = false;
        }
    }

    private void Cancel()
    {
        _error = string.Empty;
        OnClose.InvokeAsync();
    }

    // Close on backdrop click only in runtime mode (boot requires an explicit choice)
    private void HandleBackdropClick()
    {
        if (!IsBootMode && !_connecting)
            Cancel();
    }
}
