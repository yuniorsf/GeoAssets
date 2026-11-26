using System.Buffers.Binary;

namespace GeoAssets.Ingestion.Shapefile;

/// <summary>
/// Reads a <c>.shp</c> file's records directly (no geometry building): the record count, and the geometry sanity
/// over the first <see cref="SampleSize"/> non-null shapes — a layer is corrupt when more than 1 % of the sampled
/// shapes hold a coordinate whose magnitude exceeds 1e8, or when at least 1 000 shapes collapse onto fewer than 1 %
/// distinct (metre-rounded) X positions.
/// </summary>
internal static class ShapeFileProbe
{
    public const int SampleSize = 10_000;

    private const int FileHeaderLength = 100;
    private const int RecordHeaderLength = 8;
    private const double HugeCoordinate = 1e8;
    private const double MaxHugeShare = 0.01;
    private const int MinShapesForSpreadCheck = 1_000;
    private const double MinDistinctXShare = 0.01;

    public static long CountRecords(string shpPath)
    {
        using var stream = new FileStream(shpPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new BinaryReader(stream);
        long count = 0;
        stream.Position = FileHeaderLength;
        while (stream.Position + RecordHeaderLength <= stream.Length)
        {
            var contentBytes = ReadContentBytes(reader);
            if (contentBytes < 0 || stream.Position + contentBytes > stream.Length)
                break;
            stream.Position += contentBytes;
            count++;
        }

        return count;
    }

    /// <summary>A description of the defect when the sampled geometry is not sane, else <c>null</c>.</summary>
    public static string? FindGeometryDefect(string shpPath)
    {
        using var stream = new FileStream(shpPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new BinaryReader(stream);
        stream.Position = Math.Min(FileHeaderLength, stream.Length);

        var sampled = 0;
        var withHugeCoordinate = 0;
        var distinctX = new HashSet<double>();
        while (sampled < SampleSize && stream.Position + RecordHeaderLength <= stream.Length)
        {
            var contentBytes = ReadContentBytes(reader);
            var recordStart = stream.Position;
            if (contentBytes < 4 || recordStart + contentBytes > stream.Length)
                break;

            var coordinates = ReadCoordinates(reader, contentBytes);
            stream.Position = recordStart + contentBytes;
            if (coordinates.Count == 0)
                continue;

            sampled++;
            if (coordinates.Exists(value => Math.Abs(value) > HugeCoordinate))
                withHugeCoordinate++;
            distinctX.Add(Math.Round(coordinates[0]));
        }

        if (sampled == 0)
            return null;
        if (withHugeCoordinate > sampled * MaxHugeShare)
            return FormattableString.Invariant($"{withHugeCoordinate} of the first {sampled} shapes hold a coordinate beyond ±1e8");
        if (sampled >= MinShapesForSpreadCheck && distinctX.Count < sampled * MinDistinctXShare)
            return FormattableString.Invariant($"the first {sampled} shapes collapse onto {distinctX.Count} distinct X positions");
        return null;
    }

    private static int ReadContentBytes(BinaryReader reader)
    {
        reader.ReadInt32();
        var words = BinaryPrimitives.ReadInt32BigEndian(reader.ReadBytes(4));
        return words < 0 || words > int.MaxValue / 2 ? -1 : words * 2;
    }

    /// <summary>The record's coordinates as a flat x, y, x, y… list (empty for null or unknown shapes).</summary>
    private static List<double> ReadCoordinates(BinaryReader reader, int contentBytes)
    {
        var values = new List<double>();
        var shapeType = reader.ReadInt32();
        switch (shapeType)
        {
            case 1 or 11 or 21:
                if (contentBytes >= 20)
                    ReadPoints(reader, 1, values);
                break;
            case 8 or 18 or 28:
                if (contentBytes < 40)
                    break;
                reader.ReadBytes(32);
                ReadPoints(reader, ClampPoints(reader.ReadInt32(), contentBytes - 40), values);
                break;
            case 3 or 5 or 13 or 15 or 23 or 25:
                if (contentBytes < 44)
                    break;
                reader.ReadBytes(32);
                var parts = reader.ReadInt32();
                var points = reader.ReadInt32();
                if (parts < 0 || (long)parts * 4 > contentBytes - 44)
                    break;
                reader.ReadBytes(parts * 4);
                ReadPoints(reader, ClampPoints(points, contentBytes - 44 - (parts * 4)), values);
                break;
        }

        return values;
    }

    private static int ClampPoints(int declared, int remainingBytes) =>
        Math.Clamp(declared, 0, Math.Max(0, remainingBytes / 16));

    private static void ReadPoints(BinaryReader reader, int count, List<double> values)
    {
        for (var i = 0; i < count; i++)
        {
            values.Add(reader.ReadDouble());
            values.Add(reader.ReadDouble());
        }
    }
}
