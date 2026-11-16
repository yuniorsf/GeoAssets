namespace GeoAssets.Shared.Interfaces;

/// <summary>Why the selection tools have something to tell the user (XD01-310).</summary>
public enum MapSelectionNoticeKind
{
    /// <summary>Some ids were left out because the selection is full.
    /// <see cref="MapSelectionNotice.Count"/> were left out; <see cref="MapSelectionNotice.Limit"/> is the capacity.</summary>
    Overflow,

    /// <summary>The area matches more features than one gesture may select.
    /// <see cref="MapSelectionNotice.Limit"/> is the ceiling.</summary>
    TooLarge,

    /// <summary>The drawn area cannot be used (crossing lines, no area, out of range, …).</summary>
    InvalidArea,

    /// <summary>Area selection is not available in this host.</summary>
    Unsupported,

    /// <summary>The selection could not be loaded (network, server or permission failure).</summary>
    Failed
}
