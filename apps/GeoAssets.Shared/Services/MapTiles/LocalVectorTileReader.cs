using System.IO.Compression;
using GeoAssets.Core.Rendering;
using Microsoft.JSInterop;

namespace GeoAssets.Shared.Services.MapTiles;

/// <summary>
/// What a local vector tile layer's JS asks for each tile MapLibre reads (XD01-460): the Mapbox Vector Tile at one XYZ
/// address, read from the layer's <see cref="IVectorTileSource"/>, as uncompressed bytes — MapLibre decodes the protobuf
/// itself and inflates nothing a custom protocol answers. A gzip tile is inflated here, to at most
/// <see cref="MaxTileBytes"/>. Handed to JS as a <see cref="DotNetObjectReference{TValue}"/> by
/// <see cref="MapInteropService.AddLocalVectorTileLayerAsync"/>.
/// </summary>
public sealed class LocalVectorTileReader(IVectorTileSource tiles, LocalVectorTileLayer layer)
{
    /// <summary>The largest tile, once inflated, this reader hands JS.</summary>
    public const int MaxTileBytes = 16 * 1024 * 1024;

    /// <summary>
    /// The tile <paramref name="z"/>/<paramref name="x"/>/<paramref name="y"/>, or <see langword="null"/> when there is
    /// none there — no such tile in the source, or a tile outside the pyramid or the layer's zooms.
    /// </summary>
    /// <exception cref="InvalidDataException">The tile is in an encoding other than gzip, or inflates past <see cref="MaxTileBytes"/>.</exception>
    [JSInvokable("GetTileFromJs")]
    public async Task<byte[]?> GetTileFromJsAsync(int z, int x, int y)
    {
        if (z < layer.MinZoom || z > layer.MaxZoom || x < 0 || y < 0 || x >= 1 << z || y >= 1 << z)
            return null;

        var payload = await tiles.GetVectorTileAsync(new TileRequest(layer.LayerSet, new TileAddress(z, x, y)), CancellationToken.None);
        if (payload.IsEmpty)
            return null;

        return payload.ContentEncoding switch
        {
            null => payload.Content.ToArray(),
            "gzip" => await InflateAsync(payload.Content),
            var other => throw new InvalidDataException($"A local vector tile is '{other}'-encoded; only gzip tiles are inflated."),
        };
    }

    private static async Task<byte[]> InflateAsync(ReadOnlyMemory<byte> content)
    {
        using var compressed = new MemoryStream(content.ToArray(), writable: false);
        await using var inflating = new GZipStream(compressed, CompressionMode.Decompress);
        using var inflated = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await inflating.ReadAsync(buffer)) > 0)
        {
            if (inflated.Length + read > MaxTileBytes)
                throw new InvalidDataException($"A local vector tile inflates past {MaxTileBytes} bytes.");
            await inflated.WriteAsync(buffer.AsMemory(0, read));
        }

        return inflated.ToArray();
    }
}
