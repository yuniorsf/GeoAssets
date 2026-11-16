using GeoAssets.Shared.Interfaces;
using Microsoft.AspNetCore.Components;

namespace GeoAssets.Shared.Components.Map;

/// <summary>
/// The map's selection tools (XD01-310): three mutually exclusive toggle tools, a clear button,
/// the selected count (a polite live region) and the last gesture's notice. Stateless — the state
/// comes down as parameters from whoever owns the <c>MapSelectionCoordinator</c>.
/// </summary>
public partial class SelectionToolbar
{
    /// <summary>The armed tool; its button renders pressed.</summary>
    [Parameter] public MapSelectionTool Tool { get; set; }

    /// <summary>How many features are selected.</summary>
    [Parameter] public int Count { get; set; }

    /// <summary>Whether an area gesture is being resolved.</summary>
    [Parameter] public bool Busy { get; set; }

    /// <summary>The message to show, if any.</summary>
    [Parameter] public MapSelectionNotice? Notice { get; set; }

    /// <summary>The user asked for a tool: the clicked one, or <see cref="MapSelectionTool.None"/>
    /// when the pressed tool was clicked again.</summary>
    [Parameter] public EventCallback<MapSelectionTool> OnToolRequested { get; set; }

    [Parameter] public EventCallback OnClear { get; set; }

    [Parameter] public EventCallback OnNoticeDismissed { get; set; }

    /// <summary>The toggle tools, in display order.</summary>
    public static IReadOnlyList<SelectionToolOption> ToolOptions { get; } =
    [
        new(MapSelectionTool.ClickToggle, "👆", "map.select.click", "map.select.click.title"),
        new(MapSelectionTool.Box, "▭", "map.select.box", "map.select.box.title"),
        new(MapSelectionTool.Fence, "⬠", "map.select.fence", "map.select.fence.title")
    ];

    private Task RequestTool(MapSelectionTool clicked) => OnToolRequested.InvokeAsync(NextTool(Tool, clicked));

    /// <summary>Clicking the pressed tool disarms it; clicking another arms that one.</summary>
    public static MapSelectionTool NextTool(MapSelectionTool current, MapSelectionTool clicked) =>
        current == clicked ? MapSelectionTool.None : clicked;

    /// <summary>The localization key for the selected count (formatted with the count).</summary>
    public static string CountKey(int count) => count switch
    {
        <= 0 => "map.select.count.none",
        1 => "map.select.count.one",
        _ => "map.select.count.many"
    };

    /// <summary>The localization key for a notice (formatted with its count and limit).</summary>
    public static string NoticeKey(MapSelectionNoticeKind kind) => kind switch
    {
        MapSelectionNoticeKind.Overflow => "map.select.notice.overflow",
        MapSelectionNoticeKind.TooLarge => "map.select.notice.tooLarge",
        MapSelectionNoticeKind.InvalidArea => "map.select.notice.invalidArea",
        MapSelectionNoticeKind.Unsupported => "map.select.notice.unsupported",
        _ => "map.select.notice.failed"
    };
}
