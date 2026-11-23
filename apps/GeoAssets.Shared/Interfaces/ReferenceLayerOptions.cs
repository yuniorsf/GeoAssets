using GeoAssets.Core.Models;

namespace GeoAssets.Shared.Interfaces;

/// <summary>
/// How <see cref="IMapInterop.SetReferenceLayerAsync"/> draws another organization's reference features (XD01-331).
/// </summary>
/// <param name="LabelMinZoom">
/// The lowest map zoom at which each feature's label is shown, between <see cref="AssetTypeZoomRange.LowestZoom"/> and
/// <see cref="AssetTypeZoomRange.HighestZoom"/>; below it only the shapes are drawn, so a wide view is not buried in text.
/// </param>
public sealed record ReferenceLayerOptions(int LabelMinZoom)
{
    /// <summary>
    /// Throws <see cref="ArgumentNullException"/> for null <paramref name="options"/> and
    /// <see cref="ArgumentOutOfRangeException"/> when its <see cref="LabelMinZoom"/> is not a map zoom.
    /// </summary>
    public static void EnsureValid(ReferenceLayerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.LabelMinZoom is < AssetTypeZoomRange.LowestZoom or > AssetTypeZoomRange.HighestZoom)
            throw new ArgumentOutOfRangeException(
                nameof(options), options.LabelMinZoom,
                $"The label zoom must be between {AssetTypeZoomRange.LowestZoom} and {AssetTypeZoomRange.HighestZoom}.");
    }
}
