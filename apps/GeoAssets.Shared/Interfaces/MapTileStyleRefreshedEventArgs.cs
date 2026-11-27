namespace GeoAssets.Shared.Interfaces;

/// <summary>
/// One re-read of a map's tile style finished (<see cref="IMapTileLayerInterop.TileStyleRefreshed"/>, XD01-436): the map
/// now draws <see cref="Version"/>'s tiles — in <see cref="Services.MapRenderMode.MapLibre"/> once the tiles of a new
/// <c>{v}</c> were loaded (or a few seconds passed). <see cref="StartedAt"/> is when the re-read was asked for: a write
/// made after it may not be in these tiles yet.
/// </summary>
/// <param name="divId">The map's container element id.</param>
/// <param name="version">The <c>{v}</c> the map now draws; null when the style could not be read.</param>
/// <param name="startedAt">When the re-read started (the interop's <see cref="TimeProvider"/>).</param>
public sealed class MapTileStyleRefreshedEventArgs(string divId, string? version, DateTimeOffset startedAt) : EventArgs
{
    public string DivId { get; } = divId;
    public string? Version { get; } = version;
    public DateTimeOffset StartedAt { get; } = startedAt;
}
