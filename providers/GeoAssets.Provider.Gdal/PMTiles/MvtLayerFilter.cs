namespace GeoAssets.Provider.Gdal.PMTiles;

/// <summary>
/// Keeps the layers of a (decompressed) Mapbox Vector Tile a request asks for (XD01-437). A tile is a protobuf message
/// whose field 3 repeats the layers, each a message whose field 1 is its name; the filter copies every top-level field
/// but the layers it drops, byte for byte, so a kept layer is unchanged.
/// </summary>
internal static class MvtLayerFilter
{
    private const int LayersField = 3;
    private const int LayerNameField = 1;
    private const int WireVarint = 0;
    private const int Wire64Bit = 1;
    private const int WireLengthDelimited = 2;
    private const int Wire32Bit = 5;

    /// <summary>The tile with only the layers named in <paramref name="layers"/>; the same array when none is dropped.</summary>
    /// <exception cref="PMTilesFormatException">The bytes are not a well-formed vector tile.</exception>
    public static byte[] Filter(byte[] tile, HashSet<string> layers)
    {
        ArgumentNullException.ThrowIfNull(tile);
        ArgumentNullException.ThrowIfNull(layers);

        var kept = new List<Range>();
        var dropped = false;
        var position = 0;
        while (position < tile.Length)
        {
            var start = position;
            var key = ReadVarint(tile, ref position);
            var field = (int)(key >> 3);
            var wireType = (int)(key & 7);
            var (contentStart, contentLength) = SkipValue(tile, ref position, wireType);

            if (field == LayersField && wireType == WireLengthDelimited
                && !layers.Contains(LayerName(tile.AsSpan(contentStart, contentLength))))
            {
                dropped = true;
                continue;
            }

            kept.Add(start..position);
        }

        if (!dropped)
            return tile;

        using var output = new MemoryStream(tile.Length);
        foreach (var range in kept)
            output.Write(tile.AsSpan(range));
        return output.ToArray();
    }

    private static string LayerName(ReadOnlySpan<byte> layer)
    {
        var position = 0;
        while (position < layer.Length)
        {
            var key = ReadVarint(layer, ref position);
            var (start, length) = SkipValue(layer, ref position, (int)(key & 7));
            if ((int)(key >> 3) == LayerNameField && (int)(key & 7) == WireLengthDelimited)
                return System.Text.Encoding.UTF8.GetString(layer.Slice(start, length));
        }

        throw new PMTilesFormatException("A vector tile layer has no name.");
    }

    private static (int Start, int Length) SkipValue(ReadOnlySpan<byte> bytes, ref int position, int wireType)
    {
        var start = position;
        switch (wireType)
        {
            case WireVarint:
                ReadVarint(bytes, ref position);
                return (start, position - start);
            case Wire64Bit:
                return Advance(bytes, ref position, 8);
            case Wire32Bit:
                return Advance(bytes, ref position, 4);
            case WireLengthDelimited:
                var length = ReadVarint(bytes, ref position);
                if (length > (ulong)(bytes.Length - position))
                    throw new PMTilesFormatException("A vector tile field runs past the end of the tile.");
                start = position;
                position += (int)length;
                return (start, (int)length);
            default:
                throw new PMTilesFormatException($"A vector tile uses the unsupported protobuf wire type {wireType}.");
        }
    }

    private static (int Start, int Length) Advance(ReadOnlySpan<byte> bytes, ref int position, int count)
    {
        if (bytes.Length - position < count)
            throw new PMTilesFormatException("A vector tile field runs past the end of the tile.");
        var start = position;
        position += count;
        return (start, count);
    }

    private static ulong ReadVarint(ReadOnlySpan<byte> bytes, ref int position)
    {
        ulong value = 0;
        for (var shift = 0; shift < 64; shift += 7)
        {
            if (position >= bytes.Length)
                throw new PMTilesFormatException("A vector tile ends inside a varint.");

            var b = bytes[position++];
            value |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
                return value;
        }

        throw new PMTilesFormatException("A vector tile varint is longer than 64 bits.");
    }
}
