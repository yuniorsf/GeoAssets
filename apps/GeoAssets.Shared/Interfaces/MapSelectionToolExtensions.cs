namespace GeoAssets.Shared.Interfaces;

/// <summary>Wire names for <see cref="MapSelectionTool"/> as understood by <c>geoassets.js</c>.</summary>
public static class MapSelectionToolExtensions
{
    /// <summary>
    /// The lower-case tool name passed to <c>GeoAssets.setSelectionTool</c> and received back in
    /// <c>OnSelectionGeometryFromJs</c>. An explicit mapping (not <c>ToString()</c>) so renaming an
    /// enum member can never silently change the JS contract.
    /// </summary>
    public static string ToJsName(this MapSelectionTool tool) => tool switch
    {
        MapSelectionTool.None => "none",
        MapSelectionTool.ClickToggle => "click",
        MapSelectionTool.Box => "box",
        MapSelectionTool.Fence => "fence",
        _ => throw new ArgumentOutOfRangeException(nameof(tool), tool, "Unknown map selection tool.")
    };

    /// <summary>
    /// Parses a wire name produced by <see cref="ToJsName"/> (ordinal, case-sensitive). Returns
    /// <c>false</c> for <c>null</c> or any other value.
    /// </summary>
    public static bool TryParseJsName(string? name, out MapSelectionTool tool)
    {
        (var known, tool) = name switch
        {
            "none" => (true, MapSelectionTool.None),
            "click" => (true, MapSelectionTool.ClickToggle),
            "box" => (true, MapSelectionTool.Box),
            "fence" => (true, MapSelectionTool.Fence),
            _ => (false, MapSelectionTool.None)
        };
        return known;
    }
}
