using GeoAssets.Core.Interfaces;

namespace GeoAssets.Shared.Components.Projects;

/// <summary>
/// The sole subscriber to <see cref="Core.Interfaces.IProjectSessionService.CloseRequested"/>
/// (XD01-146) — mounted once in <c>MainLayout</c>, matching <c>ProviderConnectDialog</c>'s
/// always-present-boot-dialog placement. Presents the Save/Discard/Cancel choice via
/// <see cref="Shared.ConfirmDialog"/> and resolves whatever <see cref="RequestCloseAsync"/>
/// caller (explicit Close, Project switch, or logout) is awaiting.
/// </summary>
public partial class ProjectCloseGuardDialog
{
    private bool _visible;
    private TaskCompletionSource<ProjectCloseChoice>? _pending;

    protected override void OnInitialized()
    {
        base.OnInitialized();
        Session.CloseRequested = ShowAsync;
    }

    private Task<ProjectCloseChoice> ShowAsync()
    {
        _pending = new TaskCompletionSource<ProjectCloseChoice>();
        _visible = true;
        StateHasChanged();
        return _pending.Task;
    }

    private void Resolve(ProjectCloseChoice choice)
    {
        _visible = false;
        _pending?.TrySetResult(choice);
        _pending = null;
    }

    public override void Dispose()
    {
        if (ReferenceEquals(Session.CloseRequested?.Target, this))
            Session.CloseRequested = null;
        base.Dispose();
    }
}
