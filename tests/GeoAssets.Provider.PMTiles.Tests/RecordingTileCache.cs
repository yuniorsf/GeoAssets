using GeoAssets.Core.Rendering;

namespace GeoAssets.Provider.PMTiles.Tests;

/// <summary>An in-memory <see cref="ITileCache"/> recording every lookup and write.</summary>
internal sealed class RecordingTileCache : ITileCache
{
    public Dictionary<string, TilePayload> Entries { get; } = new(StringComparer.Ordinal);

    public List<string> Gets { get; } = [];

    public List<string> Sets { get; } = [];

    public Task<TilePayload?> GetAsync(TileCacheKey key, CancellationToken ct)
    {
        Gets.Add(key.Value);
        return Task.FromResult(Entries.GetValueOrDefault(key.Value));
    }

    public Task SetAsync(TileCacheKey key, TilePayload payload, CancellationToken ct)
    {
        Sets.Add(key.Value);
        Entries[key.Value] = payload;
        return Task.CompletedTask;
    }

    public Task RemoveAsync(TileRange range, CancellationToken ct) => Task.CompletedTask;
}
