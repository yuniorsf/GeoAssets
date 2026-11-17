namespace GeoAssets.Core.Models;

/// <summary>
/// Thrown when an <see cref="AssetType"/> zoom range breaks <see cref="AssetTypeZoomRange"/>'s
/// rules. <see cref="Defect"/> carries the reason as a stable, typed value.
/// </summary>
public sealed class InvalidAssetTypeZoomRangeException : InvalidOperationException
{
    public AssetTypeZoomRangeDefect Defect { get; }
    public int? MinZoom { get; }
    public int? MaxZoom { get; }

    public InvalidAssetTypeZoomRangeException(AssetTypeZoomRangeDefect defect, int? minZoom, int? maxZoom)
        : base($"The zoom range [{minZoom?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "open"}, " +
               $"{maxZoom?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "open"}] is invalid: {defect}.")
    {
        Defect = defect;
        MinZoom = minZoom;
        MaxZoom = maxZoom;
    }

    public InvalidAssetTypeZoomRangeException()
    {
    }

    public InvalidAssetTypeZoomRangeException(string message) : base(message)
    {
    }

    public InvalidAssetTypeZoomRangeException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
