namespace GeoAssets.Shared.Interfaces;

/// <summary>
/// Persistent per-feature visual state on the map (XD01-309). Sticky in <c>geoassets.js</c>'s
/// per-map registry: it survives the feature being re-rendered or scrolled out of and back into
/// the viewport, until it is set back to <see cref="Default"/> or the registry is cleared.
/// </summary>
public enum MapFeatureState
{
    /// <summary>No state — the feature renders with its own (Layer / AssetType) style.</summary>
    Default,

    /// <summary>Member of the current selection — accent colour.</summary>
    Selected,

    /// <summary>Persistently emphasised — warning colour. Distinct from the transient
    /// <see cref="IMapInterop.HighlightFeatureAsync"/> overlay, which draws on top of any state.</summary>
    Highlighted,

    /// <summary>Not eligible / not actionable — dimmed.</summary>
    Disabled,

    /// <summary>In conflict with another order — danger colour.</summary>
    Conflict,

    /// <summary>A target of the shown service order whose work is still to do (XD01-313).</summary>
    Pending,

    /// <summary>A target of the shown service order whose work is done — success colour (XD01-313).</summary>
    Done
}
