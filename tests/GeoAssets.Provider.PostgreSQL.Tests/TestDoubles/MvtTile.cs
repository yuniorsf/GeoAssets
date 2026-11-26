using System.Text;

namespace GeoAssets.Provider.PostgreSQL.Tests.TestDoubles;

/// <summary>
/// A minimal, test-only Mapbox Vector Tile 2.1 decoder (protobuf wire format, no package): layers, their features'
/// properties (keys/values resolved) and geometry as tile-unit vertices, one part per <c>MoveTo</c>.
/// </summary>
internal sealed class MvtTile
{
    private MvtTile(IReadOnlyList<Layer> layers) => Layers = layers;

    public IReadOnlyList<Layer> Layers { get; }

    public IEnumerable<string> LayerNames => Layers.Select(l => l.Name);

    public Layer this[string name] => Layers.Single(l => string.Equals(l.Name, name, StringComparison.Ordinal));

    public static MvtTile Decode(ReadOnlyMemory<byte> content)
    {
        var reader = new Reader(content.ToArray());
        var layers = new List<Layer>();
        while (!reader.End)
        {
            var (field, wire) = reader.Tag();
            if (field == 3 && wire == 2) layers.Add(DecodeLayer(reader.Message()));
            else reader.Skip(wire);
        }

        return new MvtTile(layers);
    }

    private static Layer DecodeLayer(Reader reader)
    {
        string name = string.Empty;
        uint extent = 4096;
        var keys = new List<string>();
        var values = new List<object>();
        var raw = new List<(int Type, List<uint> Tags, List<uint> Geometry)>();
        while (!reader.End)
        {
            var (field, wire) = reader.Tag();
            switch (field)
            {
                case 1: name = reader.String(); break;
                case 2: raw.Add(DecodeFeature(reader.Message())); break;
                case 3: keys.Add(reader.String()); break;
                case 4: values.Add(DecodeValue(reader.Message())); break;
                case 5: extent = (uint)reader.Varint(); break;
                default: reader.Skip(wire); break;
            }
        }

        var features = raw.Select(f => new Feature(
            f.Type,
            Enumerable.Range(0, f.Tags.Count / 2).ToDictionary(
                i => keys[(int)f.Tags[2 * i]], i => values[(int)f.Tags[(2 * i) + 1]], StringComparer.Ordinal),
            DecodeGeometry(f.Geometry))).ToList();
        return new Layer(name, extent, features);
    }

    private static (int Type, List<uint> Tags, List<uint> Geometry) DecodeFeature(Reader reader)
    {
        var type = 0;
        var tags = new List<uint>();
        var geometry = new List<uint>();
        while (!reader.End)
        {
            var (field, wire) = reader.Tag();
            switch (field)
            {
                case 2: tags.AddRange(reader.Packed()); break;
                case 3: type = (int)reader.Varint(); break;
                case 4: geometry.AddRange(reader.Packed()); break;
                default: reader.Skip(wire); break;
            }
        }

        return (type, tags, geometry);
    }

    private static object DecodeValue(Reader reader)
    {
        object value = string.Empty;
        while (!reader.End)
        {
            var (field, wire) = reader.Tag();
            value = field switch
            {
                1 => reader.String(),
                2 => BitConverter.Int32BitsToSingle((int)reader.Fixed32()),
                3 => BitConverter.Int64BitsToDouble((long)reader.Fixed64()),
                4 => (long)reader.Varint(),
                5 => reader.Varint(),
                6 => ZigZag(reader.Varint()),
                7 => reader.Varint() != 0,
                _ => SkipValue(reader, wire),
            };
        }

        return value;
    }

    private static string SkipValue(Reader reader, int wire)
    {
        reader.Skip(wire);
        return string.Empty;
    }

    private static List<List<(int X, int Y)>> DecodeGeometry(List<uint> commands)
    {
        var parts = new List<List<(int X, int Y)>>();
        int x = 0, y = 0, i = 0;
        while (i < commands.Count)
        {
            var command = commands[i++];
            var id = command & 0x7;
            var count = command >> 3;
            for (var n = 0; n < count; n++)
            {
                if (id == 7)
                {
                    parts[^1].Add(parts[^1][0]);
                    continue;
                }

                x += (int)ZigZag(commands[i++]);
                y += (int)ZigZag(commands[i++]);
                if (id == 1) parts.Add([]);
                parts[^1].Add((x, y));
            }
        }

        return parts;
    }

    private static long ZigZag(ulong value) => (long)(value >> 1) ^ -(long)(value & 1);

    internal sealed record Layer(string Name, uint Extent, IReadOnlyList<Feature> Features)
    {
        public Feature ById(string id) => Features.Single(f => Equals(f.Properties.GetValueOrDefault("id"), id));

        public IEnumerable<string> Ids => Features.Select(f => (string)f.Properties["id"]);
    }

    /// <summary>Type 1 point, 2 line string, 3 polygon; <see cref="Parts"/> in tile units, y down.</summary>
    internal sealed record Feature(int Type, IReadOnlyDictionary<string, object> Properties, IReadOnlyList<List<(int X, int Y)>> Parts);

    private sealed class Reader(byte[] buffer, int start = 0, int? end = null)
    {
        private readonly int _end = end ?? buffer.Length;
        private int _position = start;

        public bool End => _position >= _end;

        public (int Field, int Wire) Tag()
        {
            var tag = Varint();
            return ((int)(tag >> 3), (int)(tag & 0x7));
        }

        public ulong Varint()
        {
            ulong result = 0;
            for (var shift = 0; ; shift += 7)
            {
                var b = buffer[_position++];
                result |= (ulong)(b & 0x7F) << shift;
                if ((b & 0x80) == 0) return result;
            }
        }

        public uint Fixed32()
        {
            var value = BitConverter.ToUInt32(buffer, _position);
            _position += 4;
            return value;
        }

        public ulong Fixed64()
        {
            var value = BitConverter.ToUInt64(buffer, _position);
            _position += 8;
            return value;
        }

        public Reader Message()
        {
            var length = (int)Varint();
            var message = new Reader(buffer, _position, _position + length);
            _position += length;
            return message;
        }

        public string String()
        {
            var length = (int)Varint();
            var value = Encoding.UTF8.GetString(buffer, _position, length);
            _position += length;
            return value;
        }

        public List<uint> Packed()
        {
            var packed = Message();
            var values = new List<uint>();
            while (!packed.End) values.Add((uint)packed.Varint());
            return values;
        }

        public void Skip(int wire)
        {
            switch (wire)
            {
                case 0: Varint(); break;
                case 1: _position += 8; break;
                case 2: _position += (int)Varint(); break;
                case 5: _position += 4; break;
                default: throw new InvalidDataException($"Unsupported protobuf wire type {wire}.");
            }
        }
    }
}
