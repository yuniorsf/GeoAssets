using GeoAssets.Core.Models;
using Microsoft.AspNetCore.Components.Web;

namespace GeoAssets.Shared.Components.Projects;

public partial class ProjectPanel
{
    private IReadOnlyList<Project> _generalProjects = [];
    private IReadOnlyList<Project> _myForks = [];
    private bool _loading = true;
    private bool _showSaveAs;
    private string _saveAsName = string.Empty;
    private string _message = string.Empty;
    private Guid _callerId;

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        await LoadAsync();
    }

    // ── Loading ───────────────────────────────────────────────────────────────

    private async Task LoadAsync()
    {
        _loading = true;
        try
        {
            var context = await AuthService.GetAuthorizationContextAsync();
            _callerId = context.User.Id;

            if (context.User.OrganizationId is not { } organizationId)
            {
                _generalProjects = [];
                _myForks         = [];
                return;
            }

            var all = await ProjectClient.GetByOrganizationAsync(organizationId);
            (_generalProjects, _myForks) = Categorize(all, _callerId);
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>
    /// Splits every Project visible for an org into the org's General Projects (an org can have
    /// more than one, and one can have zero forks — don't assume a single General Project per
    /// org) and the caller's own User forks. Pure/static so it's directly unit-testable without
    /// a Blazor render tree (this repo has no bUnit yet — matches AssetForm's own pattern).
    /// </summary>
    public static (IReadOnlyList<Project> GeneralProjects, IReadOnlyList<Project> MyForks) Categorize(
        IReadOnlyList<Project> projects, Guid callerId)
    {
        var general = projects.Where(p => p.Kind == ProjectKind.General).ToList();
        var myForks = projects.Where(p => p.Kind == ProjectKind.User && p.CreatedByUserId == callerId).ToList();
        return (general, myForks);
    }

    // ── Open ──────────────────────────────────────────────────────────────────

    private async Task OpenAsync(Project project)
    {
        _message = string.Empty;
        try
        {
            await Session.OpenAsync(project.Id);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _message = L.GetString("projects.errorOpen", ex.Message);
        }
    }

    // ── Save ──────────────────────────────────────────────────────────────────

    private async Task SaveAsync()
    {
        if (Session.Current is null) return;
        _message = string.Empty;
        try
        {
            await Session.SaveAsync();
            _message = L["projects.saveSuccess"];
        }
        catch (Exception ex)
        {
            _message = L.GetString("projects.errorSave", ex.Message);
        }
    }

    // ── Save As ───────────────────────────────────────────────────────────────

    private void StartSaveAs()
    {
        if (Session.Current is null) return;
        _saveAsName = L.GetString("projects.saveAsDefaultName", Session.Current.Name);
        _showSaveAs = true;
    }

    private void CancelSaveAs() => _showSaveAs = false;

    private async Task ConfirmSaveAsAsync()
    {
        if (string.IsNullOrWhiteSpace(_saveAsName)) return;
        _message = string.Empty;
        try
        {
            await Session.SaveAsAsync(_saveAsName.Trim(), string.Empty);
            _showSaveAs = false;
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _message = L.GetString("projects.errorSaveAs", ex.Message);
        }
    }

    private void HandleSaveAsKey(KeyboardEventArgs ev)
    {
        if (ev.Key == "Enter") _ = ConfirmSaveAsAsync();
        if (ev.Key == "Escape") CancelSaveAs();
    }
}
