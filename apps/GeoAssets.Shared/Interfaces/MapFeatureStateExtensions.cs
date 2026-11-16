namespace GeoAssets.Shared.Interfaces;

/// <summary>Wire names for <see cref="MapFeatureState"/> as understood by <c>geoassets.js</c>.</summary>
public static class MapFeatureStateExtensions
{
    /// <summary>
    /// The lower-case state name passed to <c>GeoAssets.setFeatureStates</c> — also the suffix of
    /// the <c>geoassets-state-{name}</c> CSS class. An explicit mapping (not <c>ToString()</c>) so
    /// renaming an enum member can never silently change the JS/CSS contract.
    /// </summary>
    public static string ToJsName(this MapFeatureState state) => state switch
    {
        MapFeatureState.Default => "default",
        MapFeatureState.Selected => "selected",
        MapFeatureState.Highlighted => "highlighted",
        MapFeatureState.Disabled => "disabled",
        MapFeatureState.Conflict => "conflict",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown map feature state.")
    };
}
