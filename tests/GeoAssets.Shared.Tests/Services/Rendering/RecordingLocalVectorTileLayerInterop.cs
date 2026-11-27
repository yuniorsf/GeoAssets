using GeoAssets.Core.Rendering;
using GeoAssets.Shared.Interfaces;

namespace GeoAssets.Shared.Tests.Services.Rendering;

/// <summary>An <see cref="IMapLocalVectorTileLayerInterop"/> recording its calls.</summary>
internal sealed class RecordingLocalVectorTileLayerInterop : IMapLocalVectorTileLayerInterop
{
    public List<(string DivId, string LayerId, IVectorTileSource Tiles, LocalVectorTileLayer Layer)> Adds { get; } = [];

    public List<(string DivId, string LayerId)> Removes { get; } = [];

    public Task AddLocalVectorTileLayerAsync(string divId, string layerId, IVectorTileSource tiles, LocalVectorTileLayer layer)
    {
        Adds.Add((divId, layerId, tiles, layer));
        return Task.CompletedTask;
    }

    public Task RemoveLocalVectorTileLayerAsync(string divId, string layerId)
    {
        Removes.Add((divId, layerId));
        return Task.CompletedTask;
    }
}
