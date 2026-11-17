using System.Globalization;
using GeoAssets.Core.Models;

namespace GeoAssets.Shared.Components.Layers;

/// <summary>
/// The pure decisions behind <c>AssetTypeManager</c>'s zoom-range fields (XD01-311): reading
/// the two optional inputs, and choosing the localization keys for the error and for the
/// range shown on a type. The rules themselves are <see cref="AssetTypeZoomRange"/>'s.
/// </summary>
public static class AssetTypeZoomRangeInput
{
    /// <summary>Reads the two fields. A blank field is an open bound.</summary>
    public static AssetTypeZoomRangeInputResult Parse(string? minText, string? maxText)
    {
        var minOk = TryReadBound(minText, out var min);
        var maxOk = TryReadBound(maxText, out var max);
        if (!minOk || !maxOk)
            return Failed(AssetTypeZoomRangeInputError.NotAWholeNumber, !minOk, !maxOk);

        return AssetTypeZoomRange.FindDefect(min, max) switch
        {
            null => new AssetTypeZoomRangeInputResult(min, max, null, false, false),
            AssetTypeZoomRangeDefect.MinZoomOutOfBounds => Failed(AssetTypeZoomRangeInputError.OutOfBounds, true, false),
            AssetTypeZoomRangeDefect.MaxZoomOutOfBounds => Failed(AssetTypeZoomRangeInputError.OutOfBounds, false, true),
            _ => Failed(AssetTypeZoomRangeInputError.MinAboveMax, true, true),
        };
    }

    /// <summary>Localization key of the message for <paramref name="error"/>. The out-of-bounds
    /// message takes the lowest and highest zoom as <c>{0}</c> and <c>{1}</c>.</summary>
    public static string ErrorKey(AssetTypeZoomRangeInputError error) => error switch
    {
        AssetTypeZoomRangeInputError.OutOfBounds => "map.layers.zoomRange.error.outOfBounds",
        AssetTypeZoomRangeInputError.MinAboveMax => "map.layers.zoomRange.error.minAboveMax",
        _ => "map.layers.zoomRange.error.notWholeNumber",
    };

    /// <summary>
    /// Localization key describing a stored range: no bound, both (<c>{0}</c>–<c>{1}</c>), only
    /// a minimum (<c>{0}</c>) or only a maximum (<c>{0}</c>).
    /// </summary>
    public static string SummaryKey(int? minZoom, int? maxZoom) => (minZoom, maxZoom) switch
    {
        (null, null) => "map.layers.zoomRange.any",
        (not null, not null) => "map.layers.zoomRange.between",
        (not null, null) => "map.layers.zoomRange.from",
        _ => "map.layers.zoomRange.upTo",
    };

    /// <summary>The format arguments <see cref="SummaryKey"/>'s message expects, in order.</summary>
    public static object[] SummaryArguments(int? minZoom, int? maxZoom) => (minZoom, maxZoom) switch
    {
        (null, null) => [],
        ({ } min, { } max) => [min, max],
        ({ } min, null) => [min],
        (null, { } max) => [max],
    };

    private static bool TryReadBound(string? text, out int? zoom)
    {
        zoom = null;
        if (string.IsNullOrWhiteSpace(text)) return true;
        if (!int.TryParse(text.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsed)) return false;

        zoom = parsed;
        return true;
    }

    private static AssetTypeZoomRangeInputResult Failed(AssetTypeZoomRangeInputError error, bool minInvalid, bool maxInvalid) =>
        new(null, null, error, minInvalid, maxInvalid);
}
