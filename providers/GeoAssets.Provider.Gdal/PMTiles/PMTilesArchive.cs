using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using GeoAssets.Core.Rendering;
using Microsoft.Win32.SafeHandles;

namespace GeoAssets.Provider.Gdal.PMTiles;

/// <summary>
/// A local PMTiles v3 archive (XD01-437), read with positional reads (<see cref="RandomAccess"/>), so concurrent
/// lookups share one handle. The header, root directory and metadata are read once by <see cref="OpenAsync"/>; leaf
/// directories are read on demand and kept in a bounded cache. Every read is bounded: a directory or the metadata may
/// not exceed <see cref="MaxSectionBytes"/> (compressed or not), a tile <see cref="MaxTileBytes"/>.
/// </summary>
public sealed class PMTilesArchive : IDisposable
{
    /// <summary>The largest directory or metadata section read.</summary>
    public const int MaxSectionBytes = 64 * 1024 * 1024;

    /// <summary>The largest tile read.</summary>
    public const int MaxTileBytes = 16 * 1024 * 1024;

    /// <summary>How many leaf directories stay cached.</summary>
    public const int LeafDirectoryCacheCapacity = 64;

    private const int MaxDirectoryDepth = 4;
    private const int InitialReadBytes = 16_384;

    private readonly SafeFileHandle _file;
    private readonly PMTilesDirectory _root;
    private readonly ConcurrentDictionary<ulong, PMTilesDirectory> _leaves = new();

    private PMTilesArchive(string path, SafeFileHandle file, PMTilesHeader header, PMTilesDirectory root, IReadOnlyList<string> vectorLayerIds)
    {
        Path = path;
        _file = file;
        Header = header;
        _root = root;
        VectorLayerIds = vectorLayerIds;
    }

    /// <summary>The archive's file.</summary>
    public string Path { get; }

    /// <summary>The archive's header.</summary>
    public PMTilesHeader Header { get; }

    /// <summary>The ids of the metadata's <c>vector_layers</c>; empty when it lists none.</summary>
    public IReadOnlyList<string> VectorLayerIds { get; }

    /// <summary>Opens the archive at <paramref name="path"/>, reading its header, root directory and metadata.</summary>
    /// <exception cref="FileNotFoundException">There is no file at <paramref name="path"/>.</exception>
    /// <exception cref="PMTilesFormatException">The file is not a readable PMTiles v3 archive.</exception>
    public static async Task<PMTilesArchive> OpenAsync(string path, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var fullPath = System.IO.Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"PMTiles archive '{fullPath}' does not exist.", fullPath);

        var file = File.OpenHandle(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.Asynchronous | FileOptions.RandomAccess);
        try
        {
            var length = RandomAccess.GetLength(file);
            var start = await ReadAsync(file, 0, (int)Math.Min(InitialReadBytes, length), ct).ConfigureAwait(false);
            var header = PMTilesHeader.Parse(start);
            var rootBytes = header.RootDirectoryOffset <= (ulong)start.Length
                            && header.RootDirectoryLength <= (ulong)start.Length - header.RootDirectoryOffset
                ? start.AsMemory((int)header.RootDirectoryOffset, (int)header.RootDirectoryLength)
                : await ReadSectionAsync(file, header.RootDirectoryOffset, header.RootDirectoryLength, ct).ConfigureAwait(false);
            var root = PMTilesDirectory.Parse(PMTilesCodec.Decompress(rootBytes, header.InternalCompression, MaxSectionBytes));
            var metadata = header.MetadataLength == 0
                ? []
                : PMTilesCodec.Decompress(
                    await ReadSectionAsync(file, header.MetadataOffset, header.MetadataLength, ct).ConfigureAwait(false),
                    header.InternalCompression,
                    MaxSectionBytes);

            return new PMTilesArchive(fullPath, file, header, root, ReadVectorLayerIds(metadata));
        }
        catch
        {
            file.Dispose();
            throw;
        }
    }

    /// <summary>
    /// The stored bytes of tile <paramref name="tile"/> — still compressed with <see cref="PMTilesHeader.TileCompression"/>
    /// — or <see langword="null"/> when the archive has no such tile.
    /// </summary>
    /// <exception cref="PMTilesFormatException">The archive's directories are corrupt or nest too deep.</exception>
    public async Task<byte[]?> GetTileAsync(TileAddress tile, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (tile.Z < Header.MinZoom || tile.Z > Header.MaxZoom)
            return null;

        var tileId = PMTilesTileId.FromTile(tile);
        var directory = _root;
        for (var depth = 0; depth < MaxDirectoryDepth; depth++)
        {
            if (directory.Find(tileId) is not { } entry)
                return null;

            if (!entry.IsLeafDirectory)
            {
                if (entry.Length > MaxTileBytes)
                    throw new PMTilesFormatException($"Tile {tile} is {entry.Length.ToString(CultureInfo.InvariantCulture)} bytes, past the {MaxTileBytes}-byte limit.");
                return await ReadAsync(_file, checked(Header.TileDataOffset + entry.Offset), (int)entry.Length, ct).ConfigureAwait(false);
            }

            directory = await LeafAsync(entry, ct).ConfigureAwait(false);
        }

        throw new PMTilesFormatException($"The PMTiles directories of '{Path}' nest deeper than {MaxDirectoryDepth} levels.");
    }

    public void Dispose() => _file.Dispose();

    private async Task<PMTilesDirectory> LeafAsync(PMTilesDirectoryEntry entry, CancellationToken ct)
    {
        if (_leaves.TryGetValue(entry.Offset, out var cached))
            return cached;

        var bytes = await ReadSectionAsync(_file, checked(Header.LeafDirectoriesOffset + entry.Offset), entry.Length, ct).ConfigureAwait(false);
        var leaf = PMTilesDirectory.Parse(PMTilesCodec.Decompress(bytes, Header.InternalCompression, MaxSectionBytes));
        if (_leaves.Count >= LeafDirectoryCacheCapacity)
            _leaves.Clear();
        _leaves[entry.Offset] = leaf;
        return leaf;
    }

    private static Task<byte[]> ReadSectionAsync(SafeFileHandle file, ulong offset, ulong length, CancellationToken ct) =>
        length > MaxSectionBytes
            ? throw new PMTilesFormatException($"A PMTiles section of {length.ToString(CultureInfo.InvariantCulture)} bytes is past the {MaxSectionBytes}-byte limit.")
            : ReadAsync(file, offset, (int)length, ct);

    private static async Task<byte[]> ReadAsync(SafeFileHandle file, ulong offset, int length, CancellationToken ct)
    {
        var buffer = new byte[length];
        var filled = 0;
        while (filled < length)
        {
            var read = await RandomAccess.ReadAsync(file, buffer.AsMemory(filled), checked((long)offset + filled), ct).ConfigureAwait(false);
            if (read == 0)
                throw new PMTilesFormatException($"The PMTiles archive ends before byte {(offset + (ulong)length).ToString(CultureInfo.InvariantCulture)}.");
            filled += read;
        }

        return buffer;
    }

    private static string[] ReadVectorLayerIds(byte[] metadata)
    {
        if (metadata.Length == 0)
            return [];

        try
        {
            using var document = JsonDocument.Parse(metadata);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("vector_layers", out var layers)
                || layers.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            return [.. layers.EnumerateArray()
                .Where(layer => layer.ValueKind == JsonValueKind.Object && layer.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
                .Select(layer => layer.GetProperty("id").GetString()!)];
        }
        catch (JsonException ex)
        {
            throw new PMTilesFormatException($"The PMTiles metadata is not JSON: {ex.Message}", ex);
        }
    }
}
