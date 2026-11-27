using System.Text;

namespace GeoAssets.Provider.Gdal.Tests.PMTiles;

/// <summary>
/// A minimal Mapbox Vector Tile encoder/decoder for the tests: a tile of named layers (field 3), each with its version
/// (field 15), name (field 1) and extent (field 5) — enough to see which layers a served tile carries.
/// </summary>
internal static class TestVectorTile
{
    public static byte[] Encode(params string[] layerNames)
    {
        var tile = new MemoryStream();
        foreach (var name in layerNames)
        {
            var layer = new MemoryStream();
            Key(layer, 15, 0);
            Varint(layer, 2);
            Key(layer, 1, 2);
            var nameBytes = Encoding.UTF8.GetBytes(name);
            Varint(layer, (ulong)nameBytes.Length);
            layer.Write(nameBytes);
            Key(layer, 5, 0);
            Varint(layer, 4096);

            Key(tile, 3, 2);
            Varint(tile, (ulong)layer.Length);
            layer.WriteTo(tile);
        }

        return tile.ToArray();
    }

    public static IReadOnlyList<string> LayerNames(byte[] tile)
    {
        var names = new List<string>();
        var position = 0;
        while (position < tile.Length)
        {
            var key = ReadVarint(tile, ref position);
            var length = (int)ReadVarint(tile, ref position);
            if (key >> 3 == 3)
                names.Add(NameOf(tile.AsSpan(position, length)));
            position += length;
        }

        return names;
    }

    private static string NameOf(ReadOnlySpan<byte> layer)
    {
        var position = 0;
        while (position < layer.Length)
        {
            var key = ReadVarint(layer, ref position);
            if ((key & 7) == 0)
            {
                ReadVarint(layer, ref position);
                continue;
            }

            var length = (int)ReadVarint(layer, ref position);
            if (key >> 3 == 1)
                return Encoding.UTF8.GetString(layer.Slice(position, length));
            position += length;
        }

        return string.Empty;
    }

    private static void Key(Stream output, int field, int wireType) => Varint(output, (ulong)((field << 3) | wireType));

    private static void Varint(Stream output, ulong value)
    {
        while (value >= 0x80)
        {
            output.WriteByte((byte)(value | 0x80));
            value >>= 7;
        }

        output.WriteByte((byte)value);
    }

    private static ulong ReadVarint(ReadOnlySpan<byte> bytes, ref int position)
    {
        ulong value = 0;
        for (var shift = 0; ; shift += 7)
        {
            var b = bytes[position++];
            value |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
                return value;
        }
    }
}
