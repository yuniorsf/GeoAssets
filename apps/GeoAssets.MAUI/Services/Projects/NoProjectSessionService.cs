using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;

namespace GeoAssets.MAUI.Services.Projects;

/// <summary>
/// The app's <see cref="IProjectSessionService"/> (XD01-476): a session in which no Project is ever open, so the shared
/// layout (<c>MainLayout</c>, <c>TopBar</c>, <c>ProjectCloseGuardDialog</c>) renders with its Project controls idle and
/// the provider pool stays the user's own.
/// <para>
/// The Web's <c>BlazorProjectSessionService</c> over <c>Projects.Rest</c> would reach the kernel's Projects API through
/// the gateway, but a Project is the Web's saved workspace: opening one clears the pool and reconnects the Project's
/// providers (WFS/WMS/Shapefile entries this app has no plugin for), and every later pool change is written back into
/// it — an offline map pack, a path on this device, would land in the organization's shared Project for the Web and
/// every other device to fail to reconnect. Projects on mobile/desktop need those decisions first; until then the app
/// has none.
/// </para>
/// </summary>
internal sealed class NoProjectSessionService : IProjectSessionService
{
    public Project? Current => null;
    public Project? RawCurrent => null;
    public bool IsDirty => false;
    public IReadOnlyList<ProviderReconnectFailure> LastReconnectFailures => [];

    public event EventHandler? DirtyChanged { add { } remove { } }
    public event EventHandler<Project>? Saved { add { } remove { } }
    public event EventHandler? CurrentChanged { add { } remove { } }

    public Func<Task<ProjectCloseChoice>>? CloseRequested { get; set; }

    public Task OpenAsync(Guid projectId, CancellationToken ct = default) => Task.FromException(NoProjectClient.Unavailable());

    public Task SaveAsync(CancellationToken ct = default) => Task.FromException(NoneOpen());

    public Task SaveAsAsync(string name, string description, CancellationToken ct = default) => Task.FromException(NoneOpen());

    public Task DiscardChangesAsync(CancellationToken ct = default) => Task.FromException(NoneOpen());

    public Task CloseAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task<bool> RequestCloseAsync() => Task.FromResult(true);

    public void SetAssetTypeScope(ProjectAssetTypeScope scope) => throw NoneOpen();

    public void SetLayerScope(ProjectLayerScope scope) => throw NoneOpen();

    public void SetViewState(ProjectViewState viewState) => throw NoneOpen();

    public Task<bool> CanAsync(string permissionCode, CancellationToken ct = default) => Task.FromResult(true);

    private static InvalidOperationException NoneOpen() => new("No Project is open.");
}
