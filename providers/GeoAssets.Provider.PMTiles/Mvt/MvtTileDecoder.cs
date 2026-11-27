using System.Globalization;
using System.Text;

namespace GeoAssets.Provider.PMTiles.Mvt;

/// <summary>
/// Decodes a (decompressed) Mapbox Vector Tile, spec 2.1 (XD01-438): its layers (field 3) with their name, extent,
/// keys and values, and each layer's features with id, tags and geometry commands (<c>MoveTo</c>, <c>LineTo</c>,
/// <c>ClosePath</c>, zig-zag deltas). Bounded: at most <c>maxFeatures</c> features are decoded, in tile order; the rest
/// of the tile is skipped. A malformed tile is a <see cref="PMTilesFormatException"/>.
/// </summary>
internal static class MvtTileDecoder
{
    /// <summary>A layer's extent when it doesn't declare one.</summary>
    public const int DefaultExtent = 4096;

    private const int TileLayersField = 3;

    private const int LayerNameField = 1;
    private const int LayerFeaturesField = 2;
    private const int LayerKeysField = 3;
    private const int LayerValuesField = 4;
    private const int LayerExtentField = 5;

    private const int FeatureIdField = 1;
    private const int FeatureTagsField = 2;
    private const int FeatureTypeField = 3;
    private const int FeatureGeometryField = 4;

    private const uint MoveTo = 1;
    private const uint LineTo = 2;
    private const uint ClosePath = 7;

    /// <summary>The layers of <paramref name="tile"/>, with at most <paramref name="maxFeatures"/> features in all.</summary>
    /// <exception cref="PMTilesFormatException">The bytes are not a well-formed vector tile.</exception>
    public static IReadOnlyList<MvtLayer> Decode(byte[] tile, int maxFeatures)
    {
        ArgumentNullException.ThrowIfNull(tile);
        ArgumentOutOfRangeException.ThrowIfNegative(maxFeatures);

        var layers = new List<MvtLayer>();
        var budget = maxFeatures;
        var reader = new MvtProtoReader(tile);
        while (budget > 0 && reader.TryReadKey(out var field, out var wireType))
        {
            if (field != TileLayersField || wireType != MvtProtoReader.WireLengthDelimited)
            {
                reader.Skip(wireType);
                continue;
            }

            var (start, length) = reader.ReadLengthDelimited();
            var layer = DecodeLayer(tile.AsSpan(start, length), budget);
            budget -= layer.Features.Count;
            layers.Add(layer);
        }

        return layers;
    }

    private static MvtLayer DecodeLayer(ReadOnlySpan<byte> layer, int maxFeatures)
    {
        var name = string.Empty;
        var extent = DefaultExtent;
        var keys = new List<string>();
        var values = new List<string>();
        var features = new List<(int Start, int Length)>();

        var reader = new MvtProtoReader(layer);
        while (reader.TryReadKey(out var field, out var wireType))
        {
            switch (field)
            {
                case LayerNameField when wireType == MvtProtoReader.WireLengthDelimited:
                    name = Encoding.UTF8.GetString(reader.ReadBytes());
                    break;
                case LayerFeaturesField when wireType == MvtProtoReader.WireLengthDelimited:
                    features.Add(reader.ReadLengthDelimited());
                    break;
                case LayerKeysField when wireType == MvtProtoReader.WireLengthDelimited:
                    keys.Add(Encoding.UTF8.GetString(reader.ReadBytes()));
                    break;
                case LayerValuesField when wireType == MvtProtoReader.WireLengthDelimited:
                    values.Add(DecodeValue(reader.ReadBytes()));
                    break;
                case LayerExtentField when wireType == MvtProtoReader.WireVarint:
                    var declared = reader.ReadVarint();
                    if (declared is 0 or > int.MaxValue)
                        throw new PMTilesFormatException($"A vector tile layer declares the extent {declared.ToString(CultureInfo.InvariantCulture)}.");
                    extent = (int)declared;
                    break;
                default:
                    reader.Skip(wireType);
                    break;
            }
        }

        var decoded = new List<MvtFeature>(Math.Min(features.Count, maxFeatures));
        foreach (var (start, length) in features.Take(maxFeatures))
            decoded.Add(DecodeFeature(layer.Slice(start, length), keys, values));

        return new MvtLayer(name, extent, decoded);
    }

    private static MvtFeature DecodeFeature(ReadOnlySpan<byte> feature, List<string> keys, List<string> values)
    {
        ulong? id = null;
        var type = MvtGeometryType.Unknown;
        var tags = new List<uint>();
        var geometry = new List<uint>();

        var reader = new MvtProtoReader(feature);
        while (reader.TryReadKey(out var field, out var wireType))
        {
            switch (field)
            {
                case FeatureIdField when wireType == MvtProtoReader.WireVarint:
                    id = reader.ReadVarint();
                    break;
                case FeatureTagsField:
                    ReadUInt32s(ref reader, wireType, tags);
                    break;
                case FeatureTypeField when wireType == MvtProtoReader.WireVarint:
                    var code = reader.ReadVarint();
                    type = code <= (ulong)MvtGeometryType.Polygon ? (MvtGeometryType)code : MvtGeometryType.Unknown;
                    break;
                case FeatureGeometryField:
                    ReadUInt32s(ref reader, wireType, geometry);
                    break;
                default:
                    reader.Skip(wireType);
                    break;
            }
        }

        return new MvtFeature(id, type, Properties(tags, keys, values), Geometry(geometry, type));
    }

    /// <summary>Reads a repeated <c>uint32</c>: packed (one length-delimited run) or one varint per value.</summary>
    private static void ReadUInt32s(ref MvtProtoReader reader, int wireType, List<uint> into)
    {
        if (wireType == MvtProtoReader.WireVarint)
        {
            into.Add((uint)reader.ReadVarint());
            return;
        }

        if (wireType != MvtProtoReader.WireLengthDelimited)
        {
            reader.Skip(wireType);
            return;
        }

        var packed = new MvtProtoReader(reader.ReadBytes());
        while (packed.TryReadPackedVarint(out var value))
            into.Add((uint)value);
    }

    private static Dictionary<string, string> Properties(List<uint> tags, List<string> keys, List<string> values)
    {
        if (tags.Count % 2 != 0)
            throw new PMTilesFormatException("A vector tile feature has an odd number of tags.");

        var properties = new Dictionary<string, string>(tags.Count / 2, StringComparer.Ordinal);
        for (var i = 0; i < tags.Count; i += 2)
        {
            if (tags[i] >= keys.Count || tags[i + 1] >= values.Count)
                throw new PMTilesFormatException("A vector tile feature's tag points past its layer's keys or values.");
            properties[keys[(int)tags[i]]] = values[(int)tags[i + 1]];
        }

        return properties;
    }

    private static List<IReadOnlyList<MvtPoint>> Geometry(List<uint> commands, MvtGeometryType type)
    {
        var parts = new List<IReadOnlyList<MvtPoint>>();
        List<MvtPoint>? current = null;
        long x = 0, y = 0;
        var i = 0;
        while (i < commands.Count)
        {
            var command = commands[i++];
            var id = command & 0x7;
            var count = (int)(command >> 3);
            switch (id)
            {
                case MoveTo or LineTo:
                    if ((long)count * 2 > commands.Count - i)
                        throw new PMTilesFormatException("A vector tile geometry command runs past the end of its geometry.");
                    if (id == LineTo && current is null)
                        throw new PMTilesFormatException("A vector tile geometry starts with LineTo.");

                    for (var k = 0; k < count; k++)
                    {
                        x += ZigZag(commands[i++]);
                        y += ZigZag(commands[i++]);
                        var point = new MvtPoint(Clamp(x), Clamp(y));
                        if (id == MoveTo)
                        {
                            if (current is { Count: > 0 })
                                parts.Add(current);
                            current = [point];
                        }
                        else
                        {
                            current!.Add(point);
                        }
                    }

                    break;
                case ClosePath:
                    if (current is { Count: > 0 })
                        parts.Add(current);
                    current = null;
                    break;
                default:
                    throw new PMTilesFormatException($"A vector tile geometry uses the unknown command {id.ToString(CultureInfo.InvariantCulture)}.");
            }
        }

        if (current is { Count: > 0 } && type != MvtGeometryType.Polygon)
            parts.Add(current);
        return parts;
    }

    private static long ZigZag(uint value) => (value >> 1) ^ -(long)(value & 1);

    private static int Clamp(long value) => (int)Math.Clamp(value, int.MinValue, int.MaxValue);

    private static string DecodeValue(ReadOnlySpan<byte> value)
    {
        var text = string.Empty;
        var reader = new MvtProtoReader(value);
        while (reader.TryReadKey(out var field, out var wireType))
        {
            text = (field, wireType) switch
            {
                (1, MvtProtoReader.WireLengthDelimited) => Encoding.UTF8.GetString(reader.ReadBytes()),
                (2, MvtProtoReader.Wire32Bit) => BitConverter.Int32BitsToSingle((int)reader.ReadFixed32()).ToString("R", CultureInfo.InvariantCulture),
                (3, MvtProtoReader.Wire64Bit) => BitConverter.Int64BitsToDouble((long)reader.ReadFixed64()).ToString("R", CultureInfo.InvariantCulture),
                (4, MvtProtoReader.WireVarint) => ((long)reader.ReadVarint()).ToString(CultureInfo.InvariantCulture),
                (5, MvtProtoReader.WireVarint) => reader.ReadVarint().ToString(CultureInfo.InvariantCulture),
                (6, MvtProtoReader.WireVarint) => ZigZag64(reader.ReadVarint()).ToString(CultureInfo.InvariantCulture),
                (7, MvtProtoReader.WireVarint) => reader.ReadVarint() != 0 ? "true" : "false",
                _ => SkipValue(ref reader, wireType, text),
            };
        }

        return text;
    }

    private static string SkipValue(ref MvtProtoReader reader, int wireType, string text)
    {
        reader.Skip(wireType);
        return text;
    }

    private static long ZigZag64(ulong value) => (long)(value >> 1) ^ -(long)(value & 1);
}
