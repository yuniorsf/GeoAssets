using System.Text;

namespace GeoAssets.Core.Isp.Otdr;

/// <summary>
/// Best-effort parser for Telcordia SR-4731 ("Bellcore") <c>.SOR</c> OTDR trace files (XD01-283).
///
/// No sample <c>.SOR</c> file or vendor-specific field-offset specification was available in this
/// environment to verify against, so the byte layout below (multi-byte fields big-endian; "Map"
/// directory walk; "FxdParams"/"KeyEvents" block field offsets; round-trip propagation time) is
/// this implementation's own reconstruction of the publicly documented Bellcore SOR v2 structure,
/// not independently verified against real OTDR hardware output. Flagged explicitly in this
/// ticket's closing Jira comment — a reviewer with access to a real captured trace should confirm
/// the parsed values against it before this goes anywhere near production dispatch. What the
/// parser *does* robustly guarantee, independent of that fidelity question, is XD01-283's AC2:
/// every read is bounds-checked against the file/block length, so truncated or inconsistent input
/// throws <see cref="SorTraceParseException"/> rather than letting a raw
/// <see cref="IndexOutOfRangeException"/>/<see cref="OverflowException"/> escape as a crash.
///
/// "The fault" is picked as the most-reflective key event (highest <see cref="SorKeyEvent.ReflectanceDb"/>,
/// excluding the "not applicable" sentinel used for purely-lossy splice events) since a fiber
/// break or connector produces a strong reflection; if every event is non-reflective, the last
/// event in the trace is used instead (closest to the fiber's far end). This heuristic, like the
/// byte layout above, has no specification to follow — it is this implementation's own judgment
/// call, documented here rather than left implicit.
/// </summary>
public static class SorTraceParser
{
    private const double SpeedOfLightMetersPerSecond = 299_792_458.0;
    private const double NonReflectiveSentinelThresholdDb = -1000.0;

    public static SorTraceParseResult Parse(byte[] sorBytes)
    {
        ArgumentNullException.ThrowIfNull(sorBytes);
        var cursor = new SafeCursor(sorBytes);

        var mapBlockId = cursor.ReadFixedAsciiString(4).TrimEnd('\0');
        if (!string.Equals(mapBlockId, "Map", StringComparison.Ordinal))
            throw new SorTraceParseException($"Expected 'Map' block signature at offset 0, got '{mapBlockId}'.");

        cursor.ReadUInt16BigEndian(); // Map block version — not needed.
        cursor.ReadInt32BigEndian(); // Map block's own declared size — not needed for this parser's purposes.
        var blockCount = cursor.ReadUInt16BigEndian();
        if (blockCount is < 1 or > 64)
            throw new SorTraceParseException($"Block directory count {blockCount} is out of a sane range (1-64).");

        // Every entry here describes a block that follows sequentially after the directory ends —
        // "Map" itself was already fully consumed above and is never one of these entries in a
        // well-formed trace. If malformed input repeats "Map" here anyway, it is still added like
        // any other entry (rather than silently skipped) so the running cumulativeOffset below
        // stays correct for every block that comes after it.
        var directory = new Dictionary<string, (int Size, long StartOffset)>(StringComparer.Ordinal);
        for (var i = 0; i < blockCount; i++)
        {
            var blockId = cursor.ReadNullTerminatedAsciiString(maxLength: 64);
            cursor.ReadUInt16BigEndian(); // block version — not needed.
            var blockSize = cursor.ReadInt32BigEndian();
            if (blockSize < 0)
                throw new SorTraceParseException($"Block '{blockId}' declares a negative size ({blockSize}).");

            directory[blockId] = (blockSize, 0); // start offsets assigned below once the directory itself is fully read.
        }

        var cumulativeOffset = cursor.Position;
        var orderedBlocks = directory.Keys.ToList(); // Dictionary preserves insertion order in practice; order matters for sequential layout.
        foreach (var blockId in orderedBlocks)
        {
            var size = directory[blockId].Size;
            directory[blockId] = (size, cumulativeOffset);
            cumulativeOffset += size;
        }

        if (cumulativeOffset > sorBytes.Length)
            throw new SorTraceParseException($"Block directory declares {cumulativeOffset} total bytes, but the file is only {sorBytes.Length} bytes.");

        if (!directory.TryGetValue("FxdParams", out var fxdParams))
            throw new SorTraceParseException("Required 'FxdParams' block is missing from the directory.");
        if (!directory.TryGetValue("KeyEvents", out var keyEvents))
            throw new SorTraceParseException("Required 'KeyEvents' block is missing from the directory.");

        var (wavelengthNm, indexOfRefraction) = ParseFxdParams(sorBytes, fxdParams.StartOffset, fxdParams.Size);
        var events = ParseKeyEvents(sorBytes, keyEvents.StartOffset, keyEvents.Size);
        var faultDistanceMeters = ResolveFaultDistanceMeters(events, indexOfRefraction);

        return new SorTraceParseResult(indexOfRefraction, wavelengthNm, events, faultDistanceMeters);
    }

    private static (double WavelengthNm, double IndexOfRefraction) ParseFxdParams(byte[] bytes, long startOffset, int size)
    {
        const int WavelengthOffset = 6;
        const int IndexOfRefractionOffset = 28;
        if (size < IndexOfRefractionOffset + 4)
            throw new SorTraceParseException($"'FxdParams' block ({size} bytes) is too small to contain the required fields.");

        var cursor = new SafeCursor(bytes, startOffset + WavelengthOffset, startOffset + size);
        var wavelengthRaw = cursor.ReadUInt16BigEndian();

        cursor = new SafeCursor(bytes, startOffset + IndexOfRefractionOffset, startOffset + size);
        var indexOfRefractionRaw = cursor.ReadUInt32BigEndian();

        var wavelengthNm = wavelengthRaw / 10.0;
        var indexOfRefraction = indexOfRefractionRaw / 100_000.0;
        if (indexOfRefraction <= 0)
            throw new SorTraceParseException($"'FxdParams' index of refraction {indexOfRefraction} is out of range (must be positive).");

        return (wavelengthNm, indexOfRefraction);
    }

    private static List<SorKeyEvent> ParseKeyEvents(byte[] bytes, long startOffset, int size)
    {
        var blockEnd = startOffset + size;
        var cursor = new SafeCursor(bytes, startOffset, blockEnd);
        var count = cursor.ReadUInt16BigEndian();
        if (count is < 1 or > 1000)
            throw new SorTraceParseException($"'KeyEvents' declares {count} events, out of a sane range (1-1000).");

        var events = new List<SorKeyEvent>(count);
        for (var i = 0; i < count; i++)
        {
            var eventNumber = cursor.ReadUInt16BigEndian();
            var propagationTimePicoseconds = cursor.ReadUInt32BigEndian() * 10.0;
            var slopeRaw = cursor.ReadInt16BigEndian();
            var spliceLossRaw = cursor.ReadInt16BigEndian();
            var reflectanceRaw = cursor.ReadInt32BigEndian();
            var eventCode = cursor.ReadFixedAsciiString(6);
            var comment = cursor.ReadNullTerminatedAsciiString(maxLength: 256);
            _ = slopeRaw;

            events.Add(new SorKeyEvent(eventNumber, propagationTimePicoseconds, spliceLossRaw / 1000.0, reflectanceRaw / 1000.0, eventCode, comment));
        }

        return events;
    }

    private static double ResolveFaultDistanceMeters(IReadOnlyList<SorKeyEvent> events, double indexOfRefraction)
    {
        var reflective = events.Where(e => e.ReflectanceDb > NonReflectiveSentinelThresholdDb).ToList();
        var faultEvent = reflective.Count > 0
            ? reflective.MaxBy(e => e.ReflectanceDb)!
            : events[^1];

        var roundTripSeconds = faultEvent.PropagationTimePicoseconds * 1e-12;
        return roundTripSeconds * SpeedOfLightMetersPerSecond / indexOfRefraction / 2.0;
    }

    private sealed class SafeCursor
    {
        private readonly byte[] _bytes;
        private readonly long _end;
        public long Position { get; private set; }

        public SafeCursor(byte[] bytes, long start = 0, long? end = null)
        {
            _bytes = bytes;
            _end = end ?? bytes.Length;
            Position = start;
        }

        public ushort ReadUInt16BigEndian() => (ushort)((ReadByte() << 8) | ReadByte());

        public short ReadInt16BigEndian() => (short)ReadUInt16BigEndian();

        public uint ReadUInt32BigEndian()
        {
            uint value = 0;
            for (var i = 0; i < 4; i++)
                value = (value << 8) | (uint)ReadByte();
            return value;
        }

        public int ReadInt32BigEndian() => (int)ReadUInt32BigEndian();

        public string ReadFixedAsciiString(int length)
        {
            var bytes = new byte[length];
            for (var i = 0; i < length; i++)
                bytes[i] = (byte)ReadByte();
            return Encoding.ASCII.GetString(bytes);
        }

        public string ReadNullTerminatedAsciiString(int maxLength)
        {
            var buffer = new List<byte>(Math.Min(maxLength, 64));
            for (var i = 0; i < maxLength; i++)
            {
                var b = ReadByte();
                if (b == 0)
                    return Encoding.ASCII.GetString(buffer.ToArray());
                buffer.Add((byte)b);
            }

            throw new SorTraceParseException($"Null-terminated string exceeded the {maxLength}-byte safety bound without a terminator.");
        }

        private int ReadByte()
        {
            if (Position >= _end || Position >= _bytes.Length)
                throw new SorTraceParseException($"Unexpected end of trace data at offset {Position} (block/file boundary {_end}).");

            var value = _bytes[Position];
            Position++;
            return value;
        }
    }
}
