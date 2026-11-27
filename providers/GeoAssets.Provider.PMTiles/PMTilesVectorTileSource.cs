using System.Buffers.Binary;
using System.Security.Cryptography;
using GeoAssets.Core.Rendering;

namespace GeoAssets.Provider.PMTiles;

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
public sealed class PMTilesVectorTileSource : IVectorTileSource, IDescribedTileSource, IDisposable
{
    private readonly Lazy<Task<PMTilesArchive>> _archive;
    private PMTilesArchive? _opened;
    private string? _fileVersion;
    private TileSourceDescription? _description;

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

    /// <inheritdoc />
    /// <remarks>
    /// Read once, when the archive is opened (XD01-459): the version hashes the file's length and modification time then
    /// — the archive stays open, so every tile comes from that file and replacing it takes a restart; the layers are the
    /// metadata's <c>vector_layers</c> that are tile key segments; the zooms and bounds are the header's.
    /// </remarks>
    /// <exception cref="PMTilesFormatException">The file is not a PMTiles v3 archive of vector tiles, is corrupt, or its metadata names no layer a tile request can carry.</exception>
    public async Task<TileSourceDescription> DescribeAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var archive = await _archive.Value.WaitAsync(ct).ConfigureAwait(false);
        return _description ??= Describe(archive, _fileVersion!);
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

        var file = new FileInfo(archive.Path);
        _fileVersion = FileVersion(file.Length, file.LastWriteTimeUtc.Ticks);
        _opened = archive;
        return archive;
    }

    internal static string FileVersion(long length, long lastWriteTicks)
    {
        Span<byte> stamp = stackalloc byte[2 * sizeof(long)];
        BinaryPrimitives.WriteInt64LittleEndian(stamp, length);
        BinaryPrimitives.WriteInt64LittleEndian(stamp[sizeof(long)..], lastWriteTicks);
        return Convert.ToHexStringLower(SHA256.HashData(stamp).AsSpan(0, 8));
    }

    private TileSourceDescription Describe(PMTilesArchive archive, string version)
    {
        var layers = archive.VectorLayerIds.Where(TileKeySegment.IsValid).Distinct(StringComparer.Ordinal).ToArray();
        if (layers.Length == 0)
            throw new PMTilesFormatException($"PMTiles archive '{Path}' names no vector layer (metadata 'vector_layers') a tile request can carry.");

        var header = archive.Header;
        var maxZoom = Math.Min((int)header.MaxZoom, TileAddress.MaxZoom);
        var minZoom = Math.Min((int)header.MinZoom, maxZoom);
        var (west, south, east, north) = header.Bounds;
        BoundingBox? bounds = west is >= -180 and <= 180 && east is >= -180 and <= 180 && south is >= -90 and <= 90 && north is >= -90 and <= 90
                              && west < east && south < north
            ? new BoundingBox(west, south, east, north)
            : null;
        return new TileSourceDescription(version, layers, minZoom, maxZoom, bounds);
    }
}
