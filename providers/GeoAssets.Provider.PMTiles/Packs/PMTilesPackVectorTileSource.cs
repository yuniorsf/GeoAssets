using GeoAssets.Core.Rendering;

namespace GeoAssets.Provider.PMTiles.Packs;

/// <summary>
/// An offline vector pack's tiles as an <see cref="IVectorTileSource"/> (XD01-460), for a map that draws vector tiles
/// itself: every tile through <see cref="PMTilesPackTiles"/> — the host's tile cache first, then the pack — answered
/// whole and uncompressed (bounded by <see cref="PMTilesArchive.MaxTileBytes"/>), whatever layer set is asked: a pack
/// holds one set of layers and no tenant's.
/// </summary>
internal sealed class PMTilesPackVectorTileSource(PMTilesPackTiles tiles) : IVectorTileSource
{
    public async Task<TilePayload> GetVectorTileAsync(TileRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var bytes = await tiles.GetTileAsync(request.Address, ct).ConfigureAwait(false);
        return bytes is null || bytes.Length == 0
            ? TilePayload.Empty(TileFormats.MapboxVectorTileContentType)
            : new TilePayload(bytes, TileFormats.MapboxVectorTileContentType);
    }
}
