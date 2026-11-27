using GeoAssets.Core.Rendering;

namespace GeoAssets.Provider.PMTiles;

/// <summary>
/// <see cref="IRasterTileSource"/> over a local PMTiles v3 archive of PNG, JPEG or WebP tiles (XD01-465) — pure .NET, no
/// GDAL and no image decoding: a tile is one directory lookup by Hilbert tile id and one positional read, answered as
/// the archive stores the image, in the archive's media type (<see cref="PMTilesRasterImages.ContentType"/>), whatever
/// its pixel size (typically 256 or 512). The archive holds one image layer, so the request's layer set is not read.
/// A tile the archive doesn't have is <see cref="TilePayload.Empty"/>; one whose bytes aren't an image of the declared
/// type is refused as corrupt. The archive's images are not tenant-scoped: a host serves one only to callers who may
/// see all of it.
/// </summary>
public sealed class PMTilesRasterTileSource : IRasterTileSource, IDisposable
{
    private readonly Lazy<Task<PMTilesArchive>> _archive;
    private readonly bool _ownsArchive;
    private PMTilesArchive? _opened;

    /// <summary>A source over the archive at <paramref name="path"/>, opened on first use and disposed with the source.</summary>
    /// <exception cref="ArgumentException"><paramref name="path"/> is blank.</exception>
    public PMTilesRasterTileSource(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Path = path;
        _ownsArchive = true;
        _archive = new Lazy<Task<PMTilesArchive>>(() => OpenAsync(path), LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>A source over <paramref name="archive"/>, already open; disposing the source leaves it open.</summary>
    /// <exception cref="PMTilesFormatException">The archive doesn't hold PNG, JPEG or WebP tiles this reader serves.</exception>
    public PMTilesRasterTileSource(PMTilesArchive archive)
    {
        ArgumentNullException.ThrowIfNull(archive);
        EnsureRaster(archive);
        Path = archive.Path;
        _archive = new Lazy<Task<PMTilesArchive>>(Task.FromResult(archive));
    }

    /// <summary>The archive's file.</summary>
    public string Path { get; }

    /// <inheritdoc />
    /// <exception cref="PMTilesFormatException">The file is not a PMTiles v3 archive of PNG, JPEG or WebP tiles, or is corrupt.</exception>
    public async Task<TilePayload> GetRasterTileAsync(TileRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        var archive = await _archive.Value.WaitAsync(ct).ConfigureAwait(false);
        var tileType = archive.Header.TileType;
        var contentType = PMTilesRasterImages.ContentType(tileType);
        var stored = await archive.GetTileAsync(request.Address, ct).ConfigureAwait(false);
        if (stored is null || stored.Length == 0)
            return TilePayload.Empty(contentType);

        var compression = PMTilesCodec.Resolve(archive.Header.TileCompression, stored);
        var image = compression == PMTilesCompression.None
            ? stored
            : PMTilesCodec.Decompress(stored, compression, PMTilesArchive.MaxTileBytes);
        if (!PMTilesRasterImages.HasSignature(tileType, image))
            throw new PMTilesFormatException($"Tile {request.Address} of PMTiles archive '{Path}' is not a {tileType} image.");

        return new TilePayload(image, contentType);
    }

    public void Dispose()
    {
        if (_ownsArchive)
            Interlocked.Exchange(ref _opened, null)?.Dispose();
    }

    /// <summary>Refuses an archive whose tiles this reader can't serve as images.</summary>
    /// <exception cref="PMTilesFormatException">The tiles aren't PNG, JPEG or WebP, or are Zstandard-compressed.</exception>
    internal static void EnsureRaster(PMTilesArchive archive)
    {
        if (!PMTilesRasterImages.IsSupported(archive.Header.TileType))
            throw new PMTilesFormatException($"PMTiles archive '{archive.Path}' holds {archive.Header.TileType} tiles, not PNG, JPEG or WebP images.");

        if (archive.Header.TileCompression is not (PMTilesCompression.None or PMTilesCompression.Gzip or PMTilesCompression.Brotli or PMTilesCompression.Unknown))
            throw new PMTilesFormatException($"PMTiles archive '{archive.Path}' stores {archive.Header.TileCompression}-compressed tiles; only uncompressed, gzip or Brotli ones are read.");
    }

    private async Task<PMTilesArchive> OpenAsync(string path)
    {
        var archive = await PMTilesArchive.OpenAsync(path, CancellationToken.None).ConfigureAwait(false);
        try
        {
            EnsureRaster(archive);
        }
        catch
        {
            archive.Dispose();
            throw;
        }

        _opened = archive;
        return archive;
    }
}
