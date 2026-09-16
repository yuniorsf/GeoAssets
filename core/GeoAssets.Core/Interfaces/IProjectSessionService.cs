using GeoAssets.Core.Models;

namespace GeoAssets.Core.Interfaces;

/// <summary>
/// Owns the lifecycle of the currently-open <see cref="Project"/>: load/reconnect, dirty
/// tracking, save/discard, and the close/logout guard (XD01-142) — same Core-interface/Shared-
/// implementation split as <c>IThemeService</c>/<c>BlazorThemeService</c>.
///
/// <b>Two representations for a <see cref="ProjectKind.User"/> Project</b> (a no-op distinction
/// for <see cref="ProjectKind.General"/>, where raw and resolved are always identical): a raw
/// baseline (exactly what the server persisted, nulls preserved) and a live raw working copy
/// (starts as a copy of the baseline; each of the four scope properties stays null until its
/// first mutation via <see cref="SetAssetTypeScope"/>/<see cref="SetLayerScope"/>/
/// <see cref="SetViewState"/>, at which point it's tracked as a concrete override). <see cref="Current"/>
/// is always the <i>resolved</i> view (parent-inherited where the live copy is still null) —
/// what the UI reads and renders. <see cref="SaveAsync"/> persists the live raw working copy,
/// never the resolved view — persisting the resolved view would silently bake in every
/// never-touched scope as a permanent override on the very first save.
/// </summary>
public interface IProjectSessionService
{
    /// <summary>The resolved Project currently open, or null if none is.</summary>
    Project? Current { get; }

    /// <summary>True once anything has changed since the last open/save/discard checkpoint.</summary>
    bool IsDirty { get; }

    /// <summary>Raised whenever <see cref="IsDirty"/> changes.</summary>
    event EventHandler? DirtyChanged;

    /// <summary>Raised after a successful <see cref="SaveAsync"/>, with the saved (resolved) Project.</summary>
    event EventHandler<Project>? Saved;

    /// <summary>
    /// Raised whenever <see cref="Current"/> changes identity — <see cref="OpenAsync"/>,
    /// <see cref="CloseAsync"/>, or a copy-on-write redirect mid-<see cref="SaveAsync"/>. Not
    /// raised for a scope setter or a same-id <see cref="SaveAsync"/>, which only change
    /// <see cref="Current"/>'s contents, not which Project is open — lets any UI showing "which
    /// Project is open" (XD01-146) stay correct regardless of which component triggered the
    /// switch or close.
    /// </summary>
    event EventHandler? CurrentChanged;

    /// <summary>
    /// UI hook <see cref="RequestCloseAsync"/> invokes when the session is dirty — the
    /// subscriber must present a Save/Discard/Cancel choice to the user and return it. Actual
    /// dialog wiring belongs to a later ticket (XD01-137 child 2); this is the hook it wires
    /// into. <see cref="RequestCloseAsync"/> treats no subscriber as "cannot confirm" and
    /// refuses to close.
    /// </summary>
    Func<Task<ProjectCloseChoice>>? CloseRequested { get; set; }

    /// <summary>
    /// Opens <paramref name="projectId"/>: fetches it (resolving against its parent via
    /// <see cref="Services.ProjectResolver"/> if it's a <see cref="ProjectKind.User"/> fork),
    /// reconnects every entry in <see cref="Project.Providers"/> into the pool (a failed/unreachable
    /// entry is logged and skipped — the whole Project still opens), and updates the map from
    /// <see cref="Project.ViewState"/>.
    /// </summary>
    Task OpenAsync(Guid projectId, CancellationToken ct = default);

    /// <summary>Persists every scope that changed since the last checkpoint, then advances the baseline.</summary>
    Task SaveAsync(CancellationToken ct = default);

    /// <summary>
    /// "Save As" (XD01-145): creates a new <see cref="ProjectKind.User"/> Project — named
    /// <paramref name="name"/>/<paramref name="description"/>, forked from the currently open
    /// Project's General ancestor (itself, if <see cref="Current"/> is already
    /// <see cref="ProjectKind.General"/>) — carrying over the live working copy's current
    /// Providers/AssetTypeScope/LayerScope/ViewState (raw, not resolved — same reasoning as
    /// <see cref="SaveAsync"/>: an untouched scope stays null, inheriting from the new fork's
    /// parent, rather than being baked in). Then opens the newly created Project, which becomes
    /// <see cref="Current"/>.
    /// </summary>
    Task SaveAsAsync(string name, string description, CancellationToken ct = default);

    /// <summary>Reverts the live working copy to the raw baseline and re-runs the reconnect flow.</summary>
    Task DiscardChangesAsync(CancellationToken ct = default);

    /// <summary>
    /// Unloads <see cref="Current"/> back to no Project open: disconnects every pooled provider
    /// and clears the raw baseline/working-copy state. Does not check <see cref="IsDirty"/> or
    /// prompt — callers (XD01-146) must resolve <see cref="RequestCloseAsync"/> first.
    /// </summary>
    Task CloseAsync(CancellationToken ct = default);

    /// <summary>
    /// The single choke point for explicit Close, switching Projects, and logout. Returns
    /// <c>true</c> immediately (no prompt) when not dirty; otherwise invokes
    /// <see cref="CloseRequested"/> and acts on the result, returning whether the close may
    /// proceed.
    /// </summary>
    Task<bool> RequestCloseAsync();

    /// <summary>
    /// Overrides the AssetTypeScope on the live working copy (materializing it if this is the
    /// first touch since open) and marks the session dirty.
    /// </summary>
    void SetAssetTypeScope(ProjectAssetTypeScope scope);

    /// <summary>Overrides the LayerScope on the live working copy and marks the session dirty.</summary>
    void SetLayerScope(ProjectLayerScope scope);

    /// <summary>Overrides the ViewState on the live working copy and marks the session dirty.</summary>
    void SetViewState(ProjectViewState viewState);
}
