using System.Globalization;

namespace GeoAssets.Provider.Gdal.PMTiles;

/// <summary>A decompressed PMTiles v3 directory: its entries in ascending tile id order (XD01-437).</summary>
public sealed class PMTilesDirectory
{
    private readonly PMTilesDirectoryEntry[] _entries;

    private PMTilesDirectory(PMTilesDirectoryEntry[] entries) => _entries = entries;

    /// <summary>The entries, by ascending tile id.</summary>
    public IReadOnlyList<PMTilesDirectoryEntry> Entries => _entries;

    /// <summary>
    /// Decodes a directory: the entry count, then the tile ids (delta-encoded), the run lengths, the lengths and the
    /// offsets (each <c>offset + 1</c>, or 0 for "right after the previous entry's bytes"), all unsigned LEB128 varints.
    /// </summary>
    /// <exception cref="PMTilesFormatException">The bytes don't decode to a directory.</exception>
    public static PMTilesDirectory Parse(ReadOnlySpan<byte> bytes)
    {
        var position = 0;
        var count = ReadVarint(bytes, ref position);
        if (count > (ulong)bytes.Length)
            throw new PMTilesFormatException($"A PMTiles directory of {bytes.Length.ToString(CultureInfo.InvariantCulture)} bytes can't hold {count.ToString(CultureInfo.InvariantCulture)} entries.");

        var n = (int)count;
        var tileIds = new ulong[n];
        var runLengths = new uint[n];
        var lengths = new uint[n];
        ulong lastId = 0;
        for (var i = 0; i < n; i++)
        {
            lastId = checked(lastId + ReadVarint(bytes, ref position));
            tileIds[i] = lastId;
        }

        for (var i = 0; i < n; i++)
            runLengths[i] = ReadUInt32(bytes, ref position);
        for (var i = 0; i < n; i++)
            lengths[i] = ReadUInt32(bytes, ref position);

        var entries = new PMTilesDirectoryEntry[n];
        for (var i = 0; i < n; i++)
        {
            var raw = ReadVarint(bytes, ref position);
            var offset = raw == 0 && i > 0
                ? checked(entries[i - 1].Offset + entries[i - 1].Length)
                : raw == 0
                    ? throw new PMTilesFormatException("The first entry of a PMTiles directory can't continue a previous one.")
                    : raw - 1;
            entries[i] = new PMTilesDirectoryEntry(tileIds[i], offset, lengths[i], runLengths[i]);
        }

        return new PMTilesDirectory(entries);
    }

    /// <summary>
    /// The entry covering <paramref name="tileId"/>: the one with the greatest tile id not above it, when it is a leaf
    /// directory (the tile may be in it) or its run reaches the id; otherwise <see langword="null"/>.
    /// </summary>
    public PMTilesDirectoryEntry? Find(ulong tileId)
    {
        var low = 0;
        var high = _entries.Length - 1;
        while (low <= high)
        {
            var middle = (low + high) >>> 1;
            var id = _entries[middle].TileId;
            if (tileId > id)
                low = middle + 1;
            else if (tileId < id)
                high = middle - 1;
            else
                return _entries[middle];
        }

        if (high < 0)
            return null;

        var candidate = _entries[high];
        return candidate.IsLeafDirectory || tileId - candidate.TileId < candidate.RunLength ? candidate : null;
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> bytes, ref int position)
    {
        var value = ReadVarint(bytes, ref position);
        return value <= uint.MaxValue
            ? (uint)value
            : throw new PMTilesFormatException("A PMTiles directory length or run length exceeds 32 bits.");
    }

    private static ulong ReadVarint(ReadOnlySpan<byte> bytes, ref int position)
    {
        ulong value = 0;
        for (var shift = 0; shift < 64; shift += 7)
        {
            if (position >= bytes.Length)
                throw new PMTilesFormatException("A PMTiles directory ends inside a varint.");

            var b = bytes[position++];
            value |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
                return value;
        }

        throw new PMTilesFormatException("A PMTiles directory varint is longer than 64 bits.");
    }
}
