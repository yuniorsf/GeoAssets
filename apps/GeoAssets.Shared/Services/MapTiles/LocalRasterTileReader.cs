using GeoAssets.Core.Rendering;
using Microsoft.JSInterop;

namespace GeoAssets.Shared.Services.MapTiles;

/// <summary>
/// What a local raster tile layer's JS asks for each tile it shows (XD01-465): the image of one XYZ tile, read from the
/// layer's <see cref="IRasterTileSource"/>, as bytes JS turns into a <c>blob:</c> object URL. Handed to JS as a
/// <see cref="DotNetObjectReference{TValue}"/> by <see cref="MapInteropService.AddLocalRasterTileLayerAsync"/>.
/// </summary>
public sealed class LocalRasterTileReader(IRasterTileSource tiles, LocalRasterTileLayer layer)
{
    /// <summary>
    /// The image of tile <paramref name="z"/>/<paramref name="x"/>/<paramref name="y"/>, or <see langword="null"/> when
    /// there is none there — no such tile in the source, or a tile outside the pyramid or the layer's zooms.
    /// </summary>
    [JSInvokable("GetTileFromJs")]
    public async Task<byte[]?> GetTileFromJsAsync(int z, int x, int y)
    {
        if (z < layer.MinZoom || z > layer.MaxZoom || x < 0 || y < 0 || x >= 1 << z || y >= 1 << z)
            return null;

        var payload = await tiles.GetRasterTileAsync(new TileRequest(layer.LayerSet, new TileAddress(z, x, y)), CancellationToken.None);
        return payload.IsEmpty ? null : payload.Content.ToArray();
    }
}
