namespace GeoAssets.Shared.Interfaces;

/// <summary>
/// Map interop for selection and per-feature visual states (XD01-309) — kept separate from
/// <see cref="IMapInterop"/> so that contract (and its many test fakes) does not keep growing.
/// </summary>
public interface IMapSelectionInterop
{
    /// <summary>
    /// Sets the persistent visual state of the given features. The state is sticky: ids that are
    /// not currently rendered are remembered and styled when they next render.
    /// <see cref="MapFeatureState.Default"/> removes the state and restores the feature's own style.
    /// A <c>null</c> <paramref name="featureIds"/> throws; an empty collection makes no JS call.
    /// WebGL render mode shows state as colour/alpha only (no weight or icon change).
    /// </summary>
    Task SetFeatureStatesAsync(string divId, IReadOnlyCollection<string> featureIds, MapFeatureState state);

    /// <summary>Removes every persistent feature state on the map and restores the features' own styles.</summary>
    Task ClearFeatureStatesAsync(string divId);

    /// <summary>
    /// Arms <paramref name="tool"/> on the map, or disarms selection with
    /// <see cref="MapSelectionTool.None"/> (XD01-310). Arming a tool cancels an active feature-draw
    /// mode; snapping is off while a box or fence is being drawn and restored afterwards.
    /// </summary>
    Task SetSelectionToolAsync(string divId, MapSelectionTool tool);
}
