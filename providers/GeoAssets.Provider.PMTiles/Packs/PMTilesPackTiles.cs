using GeoAssets.Core.Rendering;

namespace GeoAssets.Provider.PMTiles.Packs;

/// <summary>
/// The tiles of an offline map pack, read through the host's <see cref="ITileCache"/> when it has one (XD01-438; on
/// MAUI the SQLCipher <c>SqliteLocalVectorTileCache</c>): a tile is looked up in the cache first, under
/// <see cref="KeyOf"/> — scope <see cref="Scope"/>, layer set <see cref="LayerSetId"/>, version = the pack's id, so a
/// changed pack never answers from another's entries — and only on a miss read from the pack (one directory lookup and
/// one positional read), then cached as stored (compressed, its encoding kept), an absent tile as an empty one so the
/// pack's directories aren't walked again for it. Answers the tile decompressed.
/// </summary>
internal sealed class PMTilesPackTiles(PMTilesPack pack, ITileCache? cache)
{
    /// <summary>The cache scope of every pack tile: a pack's features are not tenant-scoped.</summary>
    public static readonly TileCacheScope Scope = new("offline-pack", "pmtiles");

    /// <summary>The layer set id of every pack tile's cache key.</summary>
    public const string LayerSetId = "pmtiles";

    /// <summary>The cache key of <paramref name="tile"/> of the pack <paramref name="packId"/>.</summary>
    public static TileCacheKey KeyOf(string packId, TileAddress tile) => new(Scope, LayerSetId, packId, tile, TileFormat.Mvt);

    /// <summary>The decompressed vector tile at <paramref name="tile"/>, or <see langword="null"/> when the pack has none there.</summary>
    /// <exception cref="PMTilesFormatException">The pack or the cached tile is corrupt.</exception>
    public async Task<byte[]?> GetTileAsync(TileAddress tile, CancellationToken ct)
    {
        var key = KeyOf(pack.Id, tile);
        var payload = cache is null ? null : await cache.GetAsync(key, ct).ConfigureAwait(false);
        if (payload is null)
        {
            payload = await ReadFromPackAsync(tile, ct).ConfigureAwait(false);
            if (cache is not null)
                await cache.SetAsync(key, payload, ct).ConfigureAwait(false);
        }

        return payload.IsEmpty
            ? null
            : PMTilesCodec.Decompress(payload.Content, CompressionOf(payload.ContentEncoding), PMTilesArchive.MaxTileBytes);
    }

    private async Task<TilePayload> ReadFromPackAsync(TileAddress tile, CancellationToken ct)
    {
        var stored = await pack.Archive.GetTileAsync(tile, ct).ConfigureAwait(false);
        if (stored is null || stored.Length == 0)
            return TilePayload.Empty(TileFormats.MapboxVectorTileContentType);

        var compression = PMTilesCodec.Resolve(pack.Header.TileCompression, stored);
        return new TilePayload(stored, TileFormats.MapboxVectorTileContentType, PMTilesCodec.ContentEncoding(compression));
    }

    private static PMTilesCompression CompressionOf(string? contentEncoding) => contentEncoding switch
    {
        null => PMTilesCompression.None,
        "gzip" => PMTilesCompression.Gzip,
        "br" => PMTilesCompression.Brotli,
        _ => throw new PMTilesFormatException($"A cached pack tile is '{contentEncoding}'-encoded; this device reads uncompressed, gzip or Brotli tiles only."),
    };
}
