namespace GeoAssets.Core.Interfaces;

/// <summary>
/// Background safety net that silently saves the currently-open Project at a configurable
/// interval when dirty (XD01-143) — separate from, not a replacement for, the explicit
/// close/logout guard (<see cref="IProjectSessionService.RequestCloseAsync"/>).
///
/// A per-device/per-user preference (persisted client-side, not part of the Project schema —
/// it doesn't travel with the Project across devices). An autosave tick advances the baseline
/// exactly like a manual save: <see cref="IProjectSessionService.DiscardChangesAsync"/> after an
/// autosave reverts to the autosaved state, not all the way back to open time — intentional,
/// since autosave's job is bounding how much unsaved work is ever at risk, not preserving the
/// original open-time snapshot.
/// </summary>
public interface IProjectAutosaveService
{
    /// <summary>Whether autosave is currently enabled. Defaults to <c>true</c>.</summary>
    bool Enabled { get; }

    /// <summary>How often to check for unsaved changes. Defaults to 10.</summary>
    int IntervalMinutes { get; }

    /// <summary>
    /// Fired when an autosave tick's <see cref="IProjectSessionService.SaveAsync"/> call
    /// throws (e.g. it fails the capability check) — a silently-failing safety net is worse
    /// than no safety net, so a subscriber must surface this as a visible warning.
    /// </summary>
    event EventHandler<Exception>? AutosaveFailed;

    /// <summary>
    /// Fired with the tick's timestamp when an autosave tick's <see cref="IProjectSessionService.SaveAsync"/>
    /// call succeeds — lets a subscriber show a subtle "Autosaved HH:MM" indicator.
    /// </summary>
    event EventHandler<DateTimeOffset>? AutosaveSucceeded;

    /// <summary>Loads the persisted enabled/interval preference and starts the background
    /// tick loop. Must be awaited once on app startup.</summary>
    Task InitAsync(CancellationToken ct = default);

    /// <summary>Enables or disables autosave and persists the preference.</summary>
    Task SetEnabledAsync(bool enabled, CancellationToken ct = default);

    /// <summary>Changes the tick interval and persists the preference. Takes effect on the
    /// running timer immediately, without needing to reopen the Project.</summary>
    Task SetIntervalMinutesAsync(int intervalMinutes, CancellationToken ct = default);
}
