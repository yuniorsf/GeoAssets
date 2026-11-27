using GeoAssets.Core.Rendering;

namespace GeoAssets.Provider.Gdal.PMTiles;

/// <summary>
/// <see cref="IVectorTileSource"/> over a local PMTiles v3 archive of Mapbox Vector Tiles (ADR-002 D5/D6, XD01-437) — pure
/// .NET, so it runs wherever the plugin does (no native GDAL). The archive is opened on the first request (header,
/// root directory, metadata), then each tile is one directory lookup by Hilbert tile id and one positional read.
/// <para>
/// A tile is served as the archive stores it — already compressed, its <see cref="TilePayload.ContentEncoding"/> the
/// archive's tile compression (gzip, <c>br</c>, <c>zstd</c>) — when the request's layer set has every layer of the
/// archive's metadata; otherwise it is decompressed, cut down to the requested layers (<see cref="MvtLayerFilter"/>) and
/// compressed again the same way (a Zstandard archive can only be served whole). A tile the archive doesn't have, or
/// with none of the requested layers, is <see cref="TilePayload.Empty"/>. The archive's features are not tenant-scoped:
/// a host serves one only to callers who may see all of it.
/// </para>
/// </summary>
public sealed class PMTilesVectorTileSource : IVectorTileSource, IDisposable
{
    private readonly Lazy<Task<PMTilesArchive>> _archive;
    private PMTilesArchive? _opened;

    /// <summary>A source over the archive at <paramref name="path"/>, opened on first use.</summary>
    /// <exception cref="ArgumentException"><paramref name="path"/> is blank.</exception>
    public PMTilesVectorTileSource(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Path = path;
        _archive = new Lazy<Task<PMTilesArchive>>(() => OpenAsync(path), LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>The archive's file.</summary>
    public string Path { get; }

    /// <inheritdoc />
    /// <exception cref="PMTilesFormatException">The file is not a PMTiles v3 archive of vector tiles, or is corrupt.</exception>
    public async Task<TilePayload> GetVectorTileAsync(TileRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        var archive = await _archive.Value.WaitAsync(ct).ConfigureAwait(false);
        var stored = await archive.GetTileAsync(request.Address, ct).ConfigureAwait(false);
        if (stored is null || stored.Length == 0)
            return TilePayload.Empty(TileFormats.MapboxVectorTileContentType);

        var compression = PMTilesCodec.Resolve(archive.Header.TileCompression, stored);
        var requested = request.LayerSet.LayerIds.ToHashSet(StringComparer.Ordinal);
        if (archive.VectorLayerIds.Count > 0 && archive.VectorLayerIds.All(requested.Contains))
            return Payload(stored, compression);

        if (compression == PMTilesCompression.Zstd)
            throw new PMTilesFormatException($"PMTiles archive '{Path}' stores Zstandard tiles, which can't be cut down to a subset of its layers here.");

        var tile = PMTilesCodec.Decompress(stored, compression, PMTilesArchive.MaxTileBytes);
        var filtered = MvtLayerFilter.Filter(tile, requested);
        if (ReferenceEquals(filtered, tile))
            return Payload(stored, compression);

        return filtered.Length == 0
            ? TilePayload.Empty(TileFormats.MapboxVectorTileContentType)
            : Payload(PMTilesCodec.Compress(filtered, compression), compression);
    }

    public void Dispose() => Interlocked.Exchange(ref _opened, null)?.Dispose();

    private static TilePayload Payload(byte[] bytes, PMTilesCompression compression) =>
        new(bytes, TileFormats.MapboxVectorTileContentType, PMTilesCodec.ContentEncoding(compression));

    private async Task<PMTilesArchive> OpenAsync(string path)
    {
        var archive = await PMTilesArchive.OpenAsync(path, CancellationToken.None).ConfigureAwait(false);
        if (archive.Header.TileType is not (PMTilesTileType.Mvt or PMTilesTileType.Unknown))
        {
            archive.Dispose();
            throw new PMTilesFormatException($"PMTiles archive '{path}' holds {archive.Header.TileType} tiles, not vector tiles.");
        }

        _opened = archive;
        return archive;
    }
}
