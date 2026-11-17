namespace GeoAssets.Shared.Components.Layers;

/// <summary>Why the zoom-range fields of the asset-type editor were rejected.</summary>
public enum AssetTypeZoomRangeInputError
{
    /// <summary>A field holds something other than a whole number.</summary>
    NotAWholeNumber,

    /// <summary>A zoom level is outside the supported levels.</summary>
    OutOfBounds,

    /// <summary>The minimum is above the maximum.</summary>
    MinAboveMax,
}
