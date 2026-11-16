using System.Text.Json;
using GeoAssets.Shared.Interfaces;

namespace GeoAssets.Shared.Services;

/// <summary>
/// Parses the arguments of <c>MapContainer.OnSelectionGeometryFromJs</c> (XD01-310) into a
/// <see cref="MapSelectionGesture"/>. Pure, so the JS-facing guard is testable without a render
/// tree. It checks shape only; whether the area is usable is <see cref="MapSelectionRequest"/>'s job.
/// </summary>
public static class MapSelectionGestureParser
{
    /// <summary>
    /// Returns <c>false</c>, with a loggable <paramref name="rejection"/>, unless
    /// <paramref name="tool"/> is the wire name of an area tool (<c>box</c> or <c>fence</c>) and
    /// <paramref name="ringsJson"/> is a non-empty JSON array of rings, each an array of positions,
    /// each an array of exactly two numbers.
    /// </summary>
    public static bool TryParse(string? tool, string? ringsJson, bool additive, out MapSelectionGesture? gesture, out string? rejection)
    {
        gesture = null;

        if (!MapSelectionToolExtensions.TryParseJsName(tool, out var parsedTool)
            || parsedTool is not (MapSelectionTool.Box or MapSelectionTool.Fence))
        {
            rejection = "unknown or non-area selection tool";
            return false;
        }

        if (string.IsNullOrWhiteSpace(ringsJson))
        {
            rejection = "missing rings";
            return false;
        }

        double[][][]? rings;
        try
        {
            rings = JsonSerializer.Deserialize<double[][][]>(ringsJson);
        }
        catch (JsonException)
        {
            rejection = "rings are not JSON arrays of [longitude, latitude] positions";
            return false;
        }

        if (rings is not { Length: > 0 } || rings.Any(ring => ring is null || ring.Any(position => position is not { Length: 2 })))
        {
            rejection = "rings are empty or carry a position that is not a [longitude, latitude] pair";
            return false;
        }

        gesture = new MapSelectionGesture(parsedTool, rings, additive);
        rejection = null;
        return true;
    }
}
