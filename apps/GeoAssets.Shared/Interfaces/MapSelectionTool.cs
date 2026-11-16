namespace GeoAssets.Shared.Interfaces;

/// <summary>
/// The map's active multi-selection tool (XD01-310). Exactly one is armed at a time, and arming
/// one is mutually exclusive with <c>DrawToolbar</c>'s feature-drawing modes.
/// </summary>
public enum MapSelectionTool
{
    /// <summary>No selection tool — a feature click opens the feature as before.</summary>
    None,

    /// <summary>A feature click adds the feature to, or removes it from, the selection.</summary>
    ClickToggle,

    /// <summary>Two clicks draw a rectangle; the features it covers are selected.</summary>
    Box,

    /// <summary>A drawn polygon; the features it covers are selected.</summary>
    Fence
}
