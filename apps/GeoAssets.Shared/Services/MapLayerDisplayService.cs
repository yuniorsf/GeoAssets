using GeoAssets.Core.Models;
using GeoAssets.Shared.Interfaces;
using Microsoft.Extensions.Logging;

namespace GeoAssets.Shared.Services;

/// <summary>
/// What the map is told about each asset type's display (XD01-311): the user's own visibility
/// toggle and the type's zoom range (<see cref="AssetType.MinZoom"/>/<see cref="AssetType.MaxZoom"/>).
/// The two rules are independent — the map draws a type only when both allow it.
/// <para>
/// Scoped, so the state outlives the components that drive it: the layers panel can be closed
/// and reopened and still show which types are switched off, and a zoom-range push sends only
/// what changed since the last one. State is kept per map div; <see cref="Forget"/> drops it
/// when that map is destroyed, because a fresh map starts with every type visible and no range.
/// Not thread-safe — renderer context only.
/// </para>
/// </summary>
public sealed class MapLayerDisplayService(IMapInterop map, ILogger<MapLayerDisplayService> logger)
{
    private sealed class MapState
    {
        public HashSet<string> Hidden { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, (int? Min, int? Max)> Ranges { get; } = new(StringComparer.Ordinal);
    }

    private readonly Dictionary<string, MapState> _maps = new(StringComparer.Ordinal);

    /// <summary>Whether the user has <paramref name="assetTypeId"/> switched on (the default).
    /// Says nothing about the zoom range.</summary>
    public bool IsVisible(string divId, string assetTypeId) =>
        !_maps.TryGetValue(divId, out var state) || !state.Hidden.Contains(assetTypeId);

    /// <summary>Records the user's toggle and tells the map.</summary>
    public Task SetVisibleAsync(string divId, string assetTypeId, bool visible)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(divId);
        ArgumentException.ThrowIfNullOrWhiteSpace(assetTypeId);

        var state = StateFor(divId);
        if (visible) state.Hidden.Remove(assetTypeId);
        else state.Hidden.Add(assetTypeId);

        return map.SetLayerVisibilityAsync(divId, assetTypeId, visible);
    }

    /// <summary>
    /// Brings the map's zoom ranges in line with <paramref name="assetTypes"/>: one
    /// <see cref="IMapInterop.SetLayerZoomRangeAsync"/> call per type whose range differs from
    /// what this map was last told, and an open range for a type that had one and is gone.
    /// Call it whenever asset types are loaded or changed and after the map is initialized.
    /// A stored range that breaks <see cref="AssetTypeZoomRange"/>'s rules is logged and treated
    /// as open; a failed push is logged and retried on the next call. Never throws for either.
    /// </summary>
    public async Task SyncZoomRangesAsync(string divId, IEnumerable<AssetType> assetTypes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(divId);
        ArgumentNullException.ThrowIfNull(assetTypes);

        var state = StateFor(divId);
        var wanted = state.Ranges.Keys.ToDictionary(id => id, _ => ((int?)null, (int?)null), StringComparer.Ordinal);
        foreach (var type in assetTypes)
            wanted[type.Id.ToString()] = UsableRange(type);

        foreach (var (assetTypeId, range) in wanted)
        {
            if (state.Ranges.GetValueOrDefault(assetTypeId) == range) continue;

            try
            {
                await map.SetLayerZoomRangeAsync(divId, assetTypeId, range.Item1, range.Item2);
                if (range == default) state.Ranges.Remove(assetTypeId);
                else state.Ranges[assetTypeId] = range;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not push the zoom range of asset type {AssetTypeId} to map {DivId}", assetTypeId, divId);
            }
        }
    }

    /// <summary>Drops everything recorded for <paramref name="divId"/> — call when its map is destroyed.</summary>
    public void Forget(string divId) => _maps.Remove(divId);

    private (int?, int?) UsableRange(AssetType type)
    {
        if (AssetTypeZoomRange.FindDefect(type.MinZoom, type.MaxZoom) is not { } defect)
            return (type.MinZoom, type.MaxZoom);

        logger.LogWarning(
            "Asset type {AssetTypeId} has an invalid zoom range [{MinZoom}, {MaxZoom}] ({Defect}) — drawn at every zoom",
            type.Id, type.MinZoom, type.MaxZoom, defect);
        return (null, null);
    }

    private MapState StateFor(string divId)
    {
        if (!_maps.TryGetValue(divId, out var state))
            _maps[divId] = state = new MapState();
        return state;
    }
}
