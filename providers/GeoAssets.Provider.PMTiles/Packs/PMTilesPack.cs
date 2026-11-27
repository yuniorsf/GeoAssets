using System.Buffers.Binary;
using System.Security.Cryptography;

namespace GeoAssets.Provider.PMTiles.Packs;

/// <summary>
/// An offline map pack (XD01-438): an open PMTiles v3 archive of vector tiles this device can decode — Mapbox Vector
/// Tiles (or tiles of an undeclared type, read as such), stored uncompressed, gzip or Brotli — and its
/// <see cref="Id"/>, a fingerprint of its content that names it in the pack directory and in the tile cache.
/// </summary>
public sealed class PMTilesPack : IDisposable
{
    /// <summary>How many leading bytes of the file the fingerprint reads (the header, root directory and usually the metadata).</summary>
    public const int FingerprintBytes = 16_384;

    private PMTilesPack(string id, PMTilesArchive archive)
    {
        Id = id;
        Archive = archive;
    }

    /// <summary>
    /// The pack's fingerprint: 16 lowercase hex digits of the SHA-256 of its first <see cref="FingerprintBytes"/> bytes
    /// and its length — the same file always has the same id, without reading a multi-gigabyte pack whole.
    /// </summary>
    public string Id { get; }

    /// <summary>The pack's file.</summary>
    public string Path => Archive.Path;

    /// <summary>The pack's header (zoom range, bounds, compression).</summary>
    public PMTilesHeader Header => Archive.Header;

    /// <summary>The open archive.</summary>
    public PMTilesArchive Archive { get; }

    /// <summary>Opens the pack at <paramref name="path"/>, checking that this device can decode its tiles.</summary>
    /// <exception cref="FileNotFoundException">There is no file at <paramref name="path"/>.</exception>
    /// <exception cref="PMTilesFormatException">The file is not a PMTiles v3 archive, holds raster tiles, or compresses its tiles with Zstandard.</exception>
    public static async Task<PMTilesPack> OpenAsync(string path, CancellationToken ct)
    {
        var archive = await PMTilesArchive.OpenAsync(path, ct).ConfigureAwait(false);
        try
        {
            EnsureDecodable(archive);
            var id = await ComputeIdAsync(archive.Path, ct).ConfigureAwait(false);
            return new PMTilesPack(id, archive);
        }
        catch
        {
            archive.Dispose();
            throw;
        }
    }

    /// <summary>The <see cref="Id"/> the file at <paramref name="path"/> would have.</summary>
    /// <exception cref="FileNotFoundException">There is no file at <paramref name="path"/>.</exception>
    public static async Task<string> ComputeIdAsync(string path, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        using var file = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.Asynchronous);
        var length = RandomAccess.GetLength(file);
        var head = new byte[(int)Math.Min(FingerprintBytes, length)];
        var filled = 0;
        while (filled < head.Length)
        {
            var read = await RandomAccess.ReadAsync(file, head.AsMemory(filled), filled, ct).ConfigureAwait(false);
            if (read == 0)
                break;
            filled += read;
        }

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(head.AsSpan(0, filled));
        Span<byte> lengthBytes = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64LittleEndian(lengthBytes, length);
        hash.AppendData(lengthBytes);
        return Convert.ToHexStringLower(hash.GetHashAndReset().AsSpan(0, 8));
    }

    public void Dispose() => Archive.Dispose();

    private static void EnsureDecodable(PMTilesArchive archive)
    {
        if (archive.Header.TileType is not (PMTilesTileType.Mvt or PMTilesTileType.Unknown))
            throw new PMTilesFormatException($"The pack holds {archive.Header.TileType} tiles; an offline map pack holds vector tiles (MVT).");

        if (archive.Header.TileCompression is not (PMTilesCompression.None or PMTilesCompression.Gzip or PMTilesCompression.Brotli or PMTilesCompression.Unknown))
            throw new PMTilesFormatException($"The pack's tiles are {archive.Header.TileCompression}-compressed; this device reads uncompressed, gzip or Brotli tiles only.");
    }
}
