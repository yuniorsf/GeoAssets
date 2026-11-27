using GeoAssets.Shared.Interfaces;

namespace GeoAssets.Shared.Tests.Services.Rendering;

/// <summary>
/// <see cref="IMapTileLayerInterop"/> double (XD01-436): answers <see cref="Info"/> to every show, records each call in
/// <paramref name="log"/>'s <see cref="LayerRecordingMapInterop.Calls"/> (<c>show-tiles</c>, <c>hide-tiles</c>,
/// <c>hidden:[a,b]</c>) and raises <see cref="IMapTileLayerInterop.TileStyleRefreshed"/> on <see cref="Refreshed"/>.
/// </summary>
internal sealed class FakeTileLayerInterop(LayerRecordingMapInterop log) : IMapTileLayerInterop
{
    /// <summary>What a show answers; null = the style can't be read.</summary>
    public MapTileStyleInfo? Info { get; set; } = new("abc-e1-0.1", null, null, 15);

    public int Shows { get; private set; }
    public int Hides { get; private set; }

    /// <summary>The last hidden set per map.</summary>
    public Dictionary<string, IReadOnlyCollection<string>> Hidden { get; } = new(StringComparer.Ordinal);

    public event EventHandler<MapTileStyleRefreshedEventArgs>? TileStyleRefreshed;

    public Task<MapTileStyleInfo?> ShowTileLayersAsync(string divId)
    {
        Shows++;
        log.Record("show-tiles");
        return Task.FromResult(Info);
    }

    public Task HideTileLayersAsync(string divId)
    {
        Hides++;
        log.Record("hide-tiles");
        return Task.CompletedTask;
    }

    public Task SetHiddenTileFeaturesAsync(string divId, IReadOnlyCollection<string> featureIds)
    {
        Hidden[divId] = featureIds;
        log.Record("hidden:[" + string.Join(',', featureIds) + "]");
        return Task.CompletedTask;
    }

    /// <summary>A re-read of <paramref name="divId"/>'s style that started at <paramref name="startedAt"/> finished on
    /// <paramref name="version"/>.</summary>
    public void Refreshed(string divId, string? version, DateTimeOffset startedAt)
    {
        if (Info is { } info && version is not null) Info = info with { Version = version };
        TileStyleRefreshed?.Invoke(this, new MapTileStyleRefreshedEventArgs(divId, version, startedAt));
    }

    public bool HasSubscribers => TileStyleRefreshed is not null;
}
