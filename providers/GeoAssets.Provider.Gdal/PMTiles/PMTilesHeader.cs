using System.Buffers.Binary;
using System.Globalization;

namespace GeoAssets.Provider.Gdal.PMTiles;

/// <summary>
/// The 127-byte header of a PMTiles v3 archive (XD01-437): where the root directory, metadata, leaf directories and tile
/// data are, how they're compressed, the tile type and the zoom/bounds the archive covers. Little-endian throughout.
/// </summary>
public sealed record PMTilesHeader
{
    /// <summary>The header's size in bytes.</summary>
    public const int Size = 127;

    /// <summary>The spec version this reader reads.</summary>
    public const byte SupportedVersion = 3;

    private static ReadOnlySpan<byte> Magic => "PMTiles"u8;

    /// <summary>The spec version (3).</summary>
    public required byte Version { get; init; }

    public required ulong RootDirectoryOffset { get; init; }

    public required ulong RootDirectoryLength { get; init; }

    public required ulong MetadataOffset { get; init; }

    public required ulong MetadataLength { get; init; }

    public required ulong LeafDirectoriesOffset { get; init; }

    public required ulong LeafDirectoriesLength { get; init; }

    public required ulong TileDataOffset { get; init; }

    public required ulong TileDataLength { get; init; }

    public required ulong AddressedTilesCount { get; init; }

    public required ulong TileEntriesCount { get; init; }

    public required ulong TileContentsCount { get; init; }

    /// <summary>Whether the tile data is ordered by tile id (and every tile's content stored once).</summary>
    public required bool Clustered { get; init; }

    /// <summary>How the directories and metadata are compressed.</summary>
    public required PMTilesCompression InternalCompression { get; init; }

    /// <summary>How each tile is compressed.</summary>
    public required PMTilesCompression TileCompression { get; init; }

    public required PMTilesTileType TileType { get; init; }

    public required byte MinZoom { get; init; }

    public required byte MaxZoom { get; init; }

    /// <summary>West, south, east, north in degrees (stored as <c>× 10⁷</c> integers).</summary>
    public required (double West, double South, double East, double North) Bounds { get; init; }

    public required byte CenterZoom { get; init; }

    public required (double Longitude, double Latitude) Center { get; init; }

    /// <summary>Reads the header at the start of <paramref name="bytes"/>.</summary>
    /// <exception cref="PMTilesFormatException">The bytes are too short, the magic is wrong, or the version isn't 3.</exception>
    public static PMTilesHeader Parse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < Size)
            throw new PMTilesFormatException($"A PMTiles header is {Size} bytes; the file has {bytes.Length.ToString(CultureInfo.InvariantCulture)}.");
        if (!bytes[..7].SequenceEqual(Magic))
            throw new PMTilesFormatException("The file is not a PMTiles archive (no 'PMTiles' magic).");
        if (bytes[7] != SupportedVersion)
            throw new PMTilesFormatException($"PMTiles version {bytes[7].ToString(CultureInfo.InvariantCulture)} is not supported; only version {SupportedVersion.ToString(CultureInfo.InvariantCulture)} is.");

        return new PMTilesHeader
        {
            Version = bytes[7],
            RootDirectoryOffset = U64(bytes, 8),
            RootDirectoryLength = U64(bytes, 16),
            MetadataOffset = U64(bytes, 24),
            MetadataLength = U64(bytes, 32),
            LeafDirectoriesOffset = U64(bytes, 40),
            LeafDirectoriesLength = U64(bytes, 48),
            TileDataOffset = U64(bytes, 56),
            TileDataLength = U64(bytes, 64),
            AddressedTilesCount = U64(bytes, 72),
            TileEntriesCount = U64(bytes, 80),
            TileContentsCount = U64(bytes, 88),
            Clustered = bytes[96] == 1,
            InternalCompression = (PMTilesCompression)bytes[97],
            TileCompression = (PMTilesCompression)bytes[98],
            TileType = (PMTilesTileType)bytes[99],
            MinZoom = bytes[100],
            MaxZoom = bytes[101],
            Bounds = (E7(bytes, 102), E7(bytes, 106), E7(bytes, 110), E7(bytes, 114)),
            CenterZoom = bytes[118],
            Center = (E7(bytes, 119), E7(bytes, 123)),
        };
    }

    private static ulong U64(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadUInt64LittleEndian(bytes[offset..]);

    private static double E7(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadInt32LittleEndian(bytes[offset..]) / 10_000_000d;
}
