using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using GeoAssets.Core.Rendering;
using GeoAssets.Provider.Gdal.PMTiles;

namespace GeoAssets.Provider.Gdal.Tests.PMTiles;

/// <summary>
/// Writes small PMTiles v3 archives for the tests, independently of the reader under test (the spec's layout: header,
/// root directory, metadata, leaf directories, tile data), so every fixture is generated here rather than checked in.
/// </summary>
internal sealed class PMTilesTestArchive
{
    private readonly List<(ulong TileId, byte[] Content, uint RunLength)> _tiles = [];

    public PMTilesCompression InternalCompression { get; init; } = PMTilesCompression.Gzip;

    public PMTilesCompression TileCompression { get; init; } = PMTilesCompression.Gzip;

    public PMTilesTileType TileType { get; init; } = PMTilesTileType.Mvt;

    public string? MetadataJson { get; init; } = """{"vector_layers":[{"id":"roads"},{"id":"water"}]}""";

    /// <summary>When set, the root directory points at leaf directories of this many entries each.</summary>
    public int? LeafSize { get; init; }

    public byte MinZoom { get; init; }

    public byte MaxZoom { get; init; } = 14;

    /// <summary>Adds a tile (already compressed with <see cref="TileCompression"/>), repeated over <paramref name="runLength"/> consecutive ids.</summary>
    public PMTilesTestArchive Add(TileAddress tile, byte[] storedContent, uint runLength = 1)
    {
        _tiles.Add((PMTilesTileId.FromTile(tile), storedContent, runLength));
        return this;
    }

    public string WriteTo(string directory)
    {
        var path = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".pmtiles");
        File.WriteAllBytes(path, Build());
        return path;
    }

    public byte[] Build()
    {
        var tileData = new MemoryStream();
        var entries = new List<(ulong TileId, ulong Offset, uint Length, uint RunLength)>();
        foreach (var (tileId, content, runLength) in _tiles.OrderBy(t => t.TileId))
        {
            entries.Add((tileId, (ulong)tileData.Length, (uint)content.Length, runLength));
            tileData.Write(content);
        }

        byte[] root;
        var leaves = new MemoryStream();
        if (LeafSize is { } leafSize)
        {
            var pointers = new List<(ulong TileId, ulong Offset, uint Length, uint RunLength)>();
            foreach (var chunk in entries.Chunk(leafSize))
            {
                var leaf = Compress(EncodeDirectory(chunk), InternalCompression);
                pointers.Add((chunk[0].TileId, (ulong)leaves.Length, (uint)leaf.Length, 0));
                leaves.Write(leaf);
            }

            root = Compress(EncodeDirectory(pointers), InternalCompression);
        }
        else
        {
            root = Compress(EncodeDirectory(entries), InternalCompression);
        }

        var metadata = MetadataJson is null ? [] : Compress(Encoding.UTF8.GetBytes(MetadataJson), InternalCompression);
        ulong rootOffset = PMTilesHeader.Size;
        var metadataOffset = rootOffset + (ulong)root.Length;
        var leavesOffset = metadataOffset + (ulong)metadata.Length;
        var tileDataOffset = leavesOffset + (ulong)leaves.Length;

        var header = new byte[PMTilesHeader.Size];
        "PMTiles"u8.CopyTo(header);
        header[7] = 3;
        U64(header, 8, rootOffset);
        U64(header, 16, (ulong)root.Length);
        U64(header, 24, metadataOffset);
        U64(header, 32, (ulong)metadata.Length);
        U64(header, 40, leavesOffset);
        U64(header, 48, (ulong)leaves.Length);
        U64(header, 56, tileDataOffset);
        U64(header, 64, (ulong)tileData.Length);
        U64(header, 72, (ulong)_tiles.Sum(t => t.RunLength));
        U64(header, 80, (ulong)_tiles.Count);
        U64(header, 88, (ulong)_tiles.Count);
        header[96] = 1;
        header[97] = (byte)InternalCompression;
        header[98] = (byte)TileCompression;
        header[99] = (byte)TileType;
        header[100] = MinZoom;
        header[101] = MaxZoom;
        I32(header, 102, -1_800_000_000);
        I32(header, 106, -850_000_000);
        I32(header, 110, 1_800_000_000);
        I32(header, 114, 850_000_000);
        header[118] = 2;
        I32(header, 119, 10_000_000);
        I32(header, 123, -20_000_000);

        var archive = new MemoryStream();
        archive.Write(header);
        archive.Write(root);
        archive.Write(metadata);
        archive.Write(leaves.ToArray());
        archive.Write(tileData.ToArray());
        return archive.ToArray();
    }

    public static byte[] EncodeDirectory(IReadOnlyList<(ulong TileId, ulong Offset, uint Length, uint RunLength)> entries)
    {
        var output = new MemoryStream();
        Varint(output, (ulong)entries.Count);
        ulong lastId = 0;
        foreach (var entry in entries)
        {
            Varint(output, entry.TileId - lastId);
            lastId = entry.TileId;
        }

        foreach (var entry in entries)
            Varint(output, entry.RunLength);
        foreach (var entry in entries)
            Varint(output, entry.Length);
        for (var i = 0; i < entries.Count; i++)
        {
            var contiguous = i > 0 && entries[i].Offset == entries[i - 1].Offset + entries[i - 1].Length;
            Varint(output, contiguous ? 0 : entries[i].Offset + 1);
        }

        return output.ToArray();
    }

    public static byte[] Gzip(byte[] data) => Compress(data, PMTilesCompression.Gzip);

    public static byte[] Gunzip(ReadOnlyMemory<byte> data)
    {
        using var input = new MemoryStream(data.ToArray());
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return output.ToArray();
    }

    private static byte[] Compress(byte[] data, PMTilesCompression compression)
    {
        if (compression == PMTilesCompression.None)
            return data;

        using var output = new MemoryStream();
        using (Stream encoder = compression == PMTilesCompression.Gzip
                   ? new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true)
                   : new BrotliStream(output, CompressionLevel.Fastest, leaveOpen: true))
        {
            encoder.Write(data);
        }

        return output.ToArray();
    }

    private static void Varint(Stream output, ulong value)
    {
        while (value >= 0x80)
        {
            output.WriteByte((byte)(value | 0x80));
            value >>= 7;
        }

        output.WriteByte((byte)value);
    }

    private static void U64(byte[] bytes, int offset, ulong value) => BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(offset), value);

    private static void I32(byte[] bytes, int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), value);
}
