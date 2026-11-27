using System.Runtime.InteropServices;

namespace GeoAssets.Provider.PMTiles.Mvt;

/// <summary>
/// A forward-only reader of the protobuf wire format a Mapbox Vector Tile is written in (XD01-438): field keys,
/// varints, fixed 32/64-bit values and length-delimited fields. Every read is bounds-checked; a malformed tile is a
/// <see cref="PMTilesFormatException"/>, never an out-of-range read.
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal ref struct MvtProtoReader
{
    public const int WireVarint = 0;
    public const int Wire64Bit = 1;
    public const int WireLengthDelimited = 2;
    public const int Wire32Bit = 5;

    private readonly ReadOnlySpan<byte> _bytes;
    private int _position;

    public MvtProtoReader(ReadOnlySpan<byte> bytes)
    {
        _bytes = bytes;
        _position = 0;
    }

    /// <summary>Reads the next field's key, or answers <see langword="false"/> at the end of the bytes.</summary>
    public bool TryReadKey(out int field, out int wireType)
    {
        if (_position >= _bytes.Length)
        {
            field = 0;
            wireType = 0;
            return false;
        }

        var key = ReadVarint();
        field = (int)(key >> 3);
        wireType = (int)(key & 7);
        return true;
    }

    /// <summary>Reads the next varint of a packed run, or answers <see langword="false"/> at the end of the run.</summary>
    public bool TryReadPackedVarint(out ulong value)
    {
        if (_position >= _bytes.Length)
        {
            value = 0;
            return false;
        }

        value = ReadVarint();
        return true;
    }

    public ulong ReadVarint()
    {
        ulong value = 0;
        for (var shift = 0; shift < 64; shift += 7)
        {
            if (_position >= _bytes.Length)
                throw new PMTilesFormatException("A vector tile ends inside a varint.");

            var b = _bytes[_position++];
            value |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
                return value;
        }

        throw new PMTilesFormatException("A vector tile varint is longer than 64 bits.");
    }

    /// <summary>The content of a length-delimited field, as an offset and length from the start of the reader's bytes.</summary>
    public (int Start, int Length) ReadLengthDelimited()
    {
        var length = ReadVarint();
        if (length > (ulong)(_bytes.Length - _position))
            throw new PMTilesFormatException("A vector tile field runs past the end of the tile.");

        var start = _position;
        _position += (int)length;
        return (start, (int)length);
    }

    public ReadOnlySpan<byte> ReadBytes()
    {
        var (start, length) = ReadLengthDelimited();
        return _bytes.Slice(start, length);
    }

    public uint ReadFixed32() => System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(Advance(4));

    public ulong ReadFixed64() => System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(Advance(8));

    /// <summary>Skips the value of a field of <paramref name="wireType"/>.</summary>
    public void Skip(int wireType)
    {
        switch (wireType)
        {
            case WireVarint:
                ReadVarint();
                break;
            case Wire64Bit:
                Advance(8);
                break;
            case Wire32Bit:
                Advance(4);
                break;
            case WireLengthDelimited:
                ReadLengthDelimited();
                break;
            default:
                throw new PMTilesFormatException($"A vector tile uses the unsupported protobuf wire type {wireType}.");
        }
    }

    private ReadOnlySpan<byte> Advance(int count)
    {
        if (_bytes.Length - _position < count)
            throw new PMTilesFormatException("A vector tile field runs past the end of the tile.");

        var slice = _bytes.Slice(_position, count);
        _position += count;
        return slice;
    }
}
