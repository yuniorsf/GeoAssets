namespace GeoAssets.Shared.Services;

/// <summary>What <see cref="SelectionFenceOutline.Read"/> found in a selection spec.</summary>
public enum SelectionFenceOutlineKind
{
    /// <summary>The spec has no fence: there is no spec, or it was made by another strategy.</summary>
    None,

    /// <summary>The spec is a polygon fence and its rings can be drawn.</summary>
    Drawable,

    /// <summary>The spec is a polygon fence but its rings are missing or unusable.</summary>
    Malformed,
}
