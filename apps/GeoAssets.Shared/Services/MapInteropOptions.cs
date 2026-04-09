namespace GeoAssets.Shared.Services;

/// <summary>
/// Controls how <see cref="MapInteropService"/> renders features to the Leaflet map.
/// Configure via <c>builder.Services.Configure&lt;MapInteropOptions&gt;(…)</c> or
/// <c>appsettings.json → "MapInterop"</c>.
/// </summary>
public sealed class MapInteropOptions
{
    /// <summary>
    /// When <c>true</c>, all features are sent to JS in a single batch call.
    /// When <c>false</c> (default), features are sent in chunks of <see cref="BatchSize"/>,
    /// yielding to the browser event loop between chunks.
    /// </summary>
    public bool SinglePass { get; set; } = false;

    /// <summary>
    /// Number of features per batch when <see cref="SinglePass"/> is <c>false</c>.
    /// Defaults to 5. Ignored when <see cref="SinglePass"/> is <c>true</c>.
    /// </summary>
    public int BatchSize { get; set; } = 5;
}
