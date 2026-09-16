using GeoAssets.Core.Models;
using GeoAssets.Core.Theming;
using Microsoft.AspNetCore.Components;

namespace GeoAssets.Shared.Components.Layout;

public partial class TopBar
{
    // Matches ProjectsMenuItem.Id — the NavMenu panel OpenProjectsPanel reveals.
    private const string ProjectsPanelId = "projects";

    [Parameter] public string UserDisplayName { get; set; } = string.Empty;
    [Parameter] public string? OrganizationName { get; set; }
    [Parameter] public IReadOnlyList<string> Roles { get; set; } = [];

    [Parameter] public EventCallback OnSignOut { get; set; }

    private bool _userMenuOpen;
    private bool _projectMenuOpen;
    private string _projectMenuMessage = string.Empty;
    private string? _autosaveStatusMessage;
    private bool _autosaveStatusIsError;

    protected override void OnInitialized()
    {
        base.OnInitialized();
        ThemeService.ThemeChanged += OnThemeChanged;
        Session.DirtyChanged += OnSessionChanged;
        Session.Saved += OnSessionSaved;
        Session.CurrentChanged += OnSessionChanged;
        AutosaveService.AutosaveSucceeded += OnAutosaveSucceeded;
        AutosaveService.AutosaveFailed += OnAutosaveFailed;
    }

    private void OnThemeChanged(object? sender, EventArgs e) => InvokeAsync(StateHasChanged);
    private void OnSessionChanged(object? sender, EventArgs e) => InvokeAsync(StateHasChanged);
    private void OnSessionSaved(object? sender, Project e) => InvokeAsync(StateHasChanged);

    private async Task SetTheme(ThemeMode mode) => await ThemeService.SetModeAsync(mode);

    private void ToggleUserMenu() => _userMenuOpen = !_userMenuOpen;
    private void CloseUserMenu()  => _userMenuOpen = false;

    private async Task HandleSignOut()
    {
        CloseUserMenu();
        if (!await Session.RequestCloseAsync()) return;
        await OnSignOut.InvokeAsync();
    }

    // ── Project quick-access menu (XD01-146) ─────────────────────────────────

    private void ToggleProjectMenu()
    {
        _projectMenuOpen = !_projectMenuOpen;
        _projectMenuMessage = string.Empty;
    }

    private void CloseProjectMenu() => _projectMenuOpen = false;

    private async Task SaveProjectAsync()
    {
        if (Session.Current is null) return;
        try
        {
            await Session.SaveAsync();
        }
        catch (Exception ex)
        {
            _projectMenuMessage = L.GetString("projects.errorSave", ex.Message);
        }
    }

    private async Task CloseProjectAsync()
    {
        if (Session.Current is null) return;
        if (!await Session.RequestCloseAsync()) return;

        try
        {
            await Session.CloseAsync();
            _projectMenuOpen = false;
        }
        catch (Exception ex)
        {
            _projectMenuMessage = L.GetString("projects.errorClose", ex.Message);
        }
    }

    private void OpenProjectsPanel()
    {
        PanelState.Open(ProjectsPanelId);
        _projectMenuOpen = false;
    }

    // ── Autosave toggle/interval + status indicator (XD01-147) ──────────────

    private async Task OnAutosaveToggleChanged(ChangeEventArgs e) =>
        await AutosaveService.SetEnabledAsync(e.Value is true);

    private async Task OnAutosaveIntervalChanged(ChangeEventArgs e)
    {
        if (e.Value is string s && int.TryParse(s, out var minutes) && minutes > 0)
            await AutosaveService.SetIntervalMinutesAsync(minutes);
    }

    private void OnAutosaveSucceeded(object? sender, DateTimeOffset timestamp)
    {
        _autosaveStatusIsError = false;
        _autosaveStatusMessage = L.GetString("autosave.succeededAt", timestamp.ToLocalTime().ToString("HH:mm"));
        InvokeAsync(StateHasChanged);
    }

    private void OnAutosaveFailed(object? sender, Exception ex)
    {
        _autosaveStatusIsError = true;
        _autosaveStatusMessage = L.GetString("autosave.failed", ex.Message);
        InvokeAsync(StateHasChanged);
    }

    public override void Dispose()
    {
        ThemeService.ThemeChanged -= OnThemeChanged;
        Session.DirtyChanged -= OnSessionChanged;
        Session.Saved -= OnSessionSaved;
        Session.CurrentChanged -= OnSessionChanged;
        AutosaveService.AutosaveSucceeded -= OnAutosaveSucceeded;
        AutosaveService.AutosaveFailed -= OnAutosaveFailed;
        base.Dispose();
    }
}
