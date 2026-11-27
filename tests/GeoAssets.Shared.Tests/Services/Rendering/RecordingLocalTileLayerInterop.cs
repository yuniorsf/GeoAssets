using GeoAssets.Core.Rendering;
using GeoAssets.Shared.Interfaces;

namespace GeoAssets.Shared.Tests.Services.Rendering;

/// <summary>An <see cref="IMapLocalTileLayerInterop"/> recording its calls.</summary>
internal sealed class RecordingLocalTileLayerInterop : IMapLocalTileLayerInterop
{
    public List<(string DivId, string LayerId, IRasterTileSource Tiles, LocalRasterTileLayer Layer)> Adds { get; } = [];

    public List<(string DivId, string LayerId)> Removes { get; } = [];

    public Task AddLocalRasterTileLayerAsync(string divId, string layerId, IRasterTileSource tiles, LocalRasterTileLayer layer)
    {
        Adds.Add((divId, layerId, tiles, layer));
        return Task.CompletedTask;
    }

    public Task RemoveLocalRasterTileLayerAsync(string divId, string layerId)
    {
        Removes.Add((divId, layerId));
        return Task.CompletedTask;
    }
}
