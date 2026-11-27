using System.Text;

namespace GeoAssets.Provider.PMTiles.Tests;

/// <summary>
/// A Mapbox Vector Tile encoder for the decoder tests, written from the MVT 2.1 spec independently of the decoder:
/// layers (version, name, features, keys, values, extent), features (id, packed tags, type, packed geometry) and the
/// geometry commands (<c>MoveTo</c>, <c>LineTo</c>, <c>ClosePath</c>, zig-zag deltas).
/// </summary>
internal static class TestMvt
{
    public const int Point = 1;
    public const int LineString = 2;
    public const int Polygon = 3;

    /// <summary>A feature: optional id, geometry type, tile-coordinate parts and properties (string, long, double, bool or float).</summary>
    internal sealed record Feature(int Type, int[][][] Parts, IReadOnlyDictionary<string, object>? Properties = null, ulong? Id = null);

    internal sealed record Layer(string Name, IReadOnlyList<Feature> Features, int? Extent = 4096);

    public static byte[] Encode(params Layer[] layers)
    {
        var tile = new MemoryStream();
        foreach (var layer in layers)
            LengthDelimited(tile, 3, EncodeLayer(layer));
        return tile.ToArray();
    }

    public static Feature PointAt(int x, int y, IReadOnlyDictionary<string, object>? properties = null, ulong? id = null) =>
        new(Point, [[[x, y]]], properties, id);

    public static Feature Line(params int[][] vertices) => new(LineString, [vertices]);

    public static Feature Area(params int[][][] rings) => new(Polygon, rings);

    private static byte[] EncodeLayer(Layer layer)
    {
        var keys = new List<string>();
        var values = new List<object>();
        var output = new MemoryStream();
        Varint(output, Key(15, 0));
        Varint(output, 2);
        LengthDelimited(output, 1, Encoding.UTF8.GetBytes(layer.Name));

        foreach (var feature in layer.Features)
        {
            var encoded = new MemoryStream();
            if (feature.Id is { } id)
            {
                Varint(encoded, Key(1, 0));
                Varint(encoded, id);
            }

            if (feature.Properties is { Count: > 0 } properties)
            {
                var tags = new List<ulong>();
                foreach (var (key, value) in properties)
                {
                    tags.Add((ulong)IndexOf(keys, key));
                    tags.Add((ulong)IndexOf(values, value));
                }

                Packed(encoded, 2, tags);
            }

            Varint(encoded, Key(3, 0));
            Varint(encoded, (ulong)feature.Type);
            Packed(encoded, 4, Geometry(feature));
            LengthDelimited(output, 2, encoded.ToArray());
        }

        foreach (var key in keys)
            LengthDelimited(output, 3, Encoding.UTF8.GetBytes(key));
        foreach (var value in values)
            LengthDelimited(output, 4, EncodeValue(value));
        if (layer.Extent is { } extent)
        {
            Varint(output, Key(5, 0));
            Varint(output, (ulong)extent);
        }

        return output.ToArray();
    }

    private static List<ulong> Geometry(Feature feature)
    {
        var commands = new List<ulong>();
        long x = 0, y = 0;
        void Move(int[] point)
        {
            commands.Add(ZigZag(point[0] - x));
            commands.Add(ZigZag(point[1] - y));
            x = point[0];
            y = point[1];
        }

        if (feature.Type == Point)
        {
            var points = feature.Parts.SelectMany(p => p).ToArray();
            commands.Add(Command(1, points.Length));
            foreach (var point in points)
                Move(point);
            return commands;
        }

        foreach (var part in feature.Parts)
        {
            commands.Add(Command(1, 1));
            Move(part[0]);
            commands.Add(Command(2, part.Length - 1));
            foreach (var point in part.Skip(1))
                Move(point);
            if (feature.Type == Polygon)
                commands.Add(Command(7, 1));
        }

        return commands;
    }

    private static byte[] EncodeValue(object value)
    {
        var output = new MemoryStream();
        switch (value)
        {
            case string s:
                LengthDelimited(output, 1, Encoding.UTF8.GetBytes(s));
                break;
            case float f:
                Varint(output, Key(2, 5));
                output.Write(BitConverter.GetBytes(f));
                break;
            case double d:
                Varint(output, Key(3, 1));
                output.Write(BitConverter.GetBytes(d));
                break;
            case long l when l < 0:
                Varint(output, Key(6, 0));
                Varint(output, (ulong)((l << 1) ^ (l >> 63)));
                break;
            case long l:
                Varint(output, Key(4, 0));
                Varint(output, (ulong)l);
                break;
            case bool b:
                Varint(output, Key(7, 0));
                Varint(output, b ? 1UL : 0UL);
                break;
            default:
                throw new ArgumentException($"Unsupported value {value}.", nameof(value));
        }

        return output.ToArray();
    }

    private static int IndexOf<T>(List<T> list, T item) where T : notnull
    {
        var index = list.IndexOf(item);
        if (index >= 0)
            return index;
        list.Add(item);
        return list.Count - 1;
    }

    private static ulong Command(int id, int count) => (ulong)((id & 7) | (count << 3));

    private static ulong ZigZag(long n) => (ulong)((n << 1) ^ (n >> 63));

    private static ulong Key(int field, int wireType) => (ulong)((field << 3) | wireType);

    private static void Packed(Stream output, int field, List<ulong> values)
    {
        var packed = new MemoryStream();
        foreach (var value in values)
            Varint(packed, value);
        LengthDelimited(output, field, packed.ToArray());
    }

    private static void LengthDelimited(Stream output, int field, byte[] content)
    {
        Varint(output, Key(field, 2));
        Varint(output, (ulong)content.Length);
        output.Write(content);
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
}
