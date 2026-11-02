using System.Text;
using FluentAssertions;
using GeoAssets.Core.Isp.Otdr;
using Xunit;

namespace GeoAssets.Core.Tests.Isp.Otdr;

/// <summary>
/// XD01-283's AC2: every read inside <see cref="SorTraceParser"/> is bounds-checked, so
/// malformed/truncated/structurally-inconsistent <c>.SOR</c> bytes always surface as
/// <see cref="SorTraceParseException"/>, never a raw framework exception. Also covers the
/// "most reflective event is the fault" heuristic (and its all-non-reflective fallback) and the
/// happy-path wavelength/index-of-refraction/fault-distance parsing. The byte layout constructed
/// here mirrors <see cref="SorTraceParser"/>'s own doc comment and <c>ParseFxdParams</c>/
/// <c>ParseKeyEvents</c> offsets exactly — see this file's private builders.
/// </summary>
public class SorTraceParserTests
{
    private const double SpeedOfLightMetersPerSecond = 299_792_458.0;

    private sealed record KeyEventSpec(
        double PropagationTimePicoseconds,
        double SpliceLossDb,
        double ReflectanceDb,
        string EventCode,
        string Comment);

    // ── Well-formed trace construction ──────────────────────────────────────

    private static byte[] BuildValidSorBytes(
        double wavelengthNm,
        double indexOfRefraction,
        IReadOnlyList<KeyEventSpec> events)
    {
        var fxdParams = BuildFxdParamsBlock(wavelengthNm, indexOfRefraction);
        var keyEvents = BuildKeyEventsBlock(events);
        return AssembleTrace([("FxdParams", fxdParams), ("KeyEvents", keyEvents)]);
    }

    private static byte[] AssembleTrace(IReadOnlyList<(string BlockId, byte[] Content)> blocks)
    {
        var buffer = new List<byte>();
        buffer.AddRange(Encoding.ASCII.GetBytes("Map"));
        buffer.Add(0); // "Map" fixed 4-byte signature, null-padded.
        WriteUInt16BE(buffer, 1); // Map block version — unused by the parser.
        WriteInt32BE(buffer, 0); // Map block's own declared size — unused by the parser.
        WriteUInt16BE(buffer, (ushort)blocks.Count); // blockCount

        foreach (var (blockId, content) in blocks)
        {
            buffer.AddRange(Encoding.ASCII.GetBytes(blockId));
            buffer.Add(0); // null terminator
            WriteUInt16BE(buffer, 1); // block version — unused by the parser.
            WriteInt32BE(buffer, content.Length);
        }

        foreach (var (_, content) in blocks)
            buffer.AddRange(content);

        return buffer.ToArray();
    }

    private static byte[] BuildFxdParamsBlock(double wavelengthNm, double indexOfRefraction)
    {
        // Offsets per SorTraceParser.ParseFxdParams: WavelengthOffset = 6 (UInt16BE),
        // IndexOfRefractionOffset = 28 (UInt32BE); block must be >= 32 bytes.
        var block = new byte[32];
        WriteUInt16BEAt(block, 6, (ushort)Math.Round(wavelengthNm * 10.0));
        WriteUInt32BEAt(block, 28, (uint)Math.Round(indexOfRefraction * 100_000.0));
        return block;
    }

    private static byte[] BuildKeyEventsBlock(IReadOnlyList<KeyEventSpec> events)
    {
        var buffer = new List<byte>();
        WriteUInt16BE(buffer, (ushort)events.Count);

        var eventNumber = 1;
        foreach (var spec in events)
        {
            WriteUInt16BE(buffer, (ushort)eventNumber++);
            WriteUInt32BE(buffer, (uint)Math.Round(spec.PropagationTimePicoseconds / 10.0));
            WriteInt16BE(buffer, 0); // slope — unused by the parser.
            WriteInt16BE(buffer, (short)Math.Round(spec.SpliceLossDb * 1000.0));
            WriteInt32BE(buffer, (int)Math.Round(spec.ReflectanceDb * 1000.0));
            var codeBytes = Encoding.ASCII.GetBytes(spec.EventCode.PadRight(6)[..6]);
            buffer.AddRange(codeBytes);
            buffer.AddRange(Encoding.ASCII.GetBytes(spec.Comment));
            buffer.Add(0); // null terminator
        }

        return buffer.ToArray();
    }

    private static void WriteUInt16BE(List<byte> buffer, ushort value)
    {
        buffer.Add((byte)(value >> 8));
        buffer.Add((byte)value);
    }

    private static void WriteInt16BE(List<byte> buffer, short value) => WriteUInt16BE(buffer, (ushort)value);

    private static void WriteUInt32BE(List<byte> buffer, uint value)
    {
        buffer.Add((byte)(value >> 24));
        buffer.Add((byte)(value >> 16));
        buffer.Add((byte)(value >> 8));
        buffer.Add((byte)value);
    }

    private static void WriteInt32BE(List<byte> buffer, int value) => WriteUInt32BE(buffer, (uint)value);

    private static void WriteUInt16BEAt(byte[] bytes, int offset, ushort value)
    {
        bytes[offset] = (byte)(value >> 8);
        bytes[offset + 1] = (byte)value;
    }

    private static void WriteUInt32BEAt(byte[] bytes, int offset, uint value)
    {
        bytes[offset] = (byte)(value >> 24);
        bytes[offset + 1] = (byte)(value >> 16);
        bytes[offset + 2] = (byte)(value >> 8);
        bytes[offset + 3] = (byte)value;
    }

    // ── Happy path ───────────────────────────────────────────────────────────

    [Fact]
    public void Parse_WellFormedTraceWithOneKeyEvent_ParsesExpectedWavelengthIndexOfRefractionAndFaultDistance()
    {
        const double wavelengthNm = 1550.0;
        const double indexOfRefraction = 1.4682;
        const double propagationTimePicoseconds = 1_000_000.0; // 1e6 ps, divisible by 10.
        var events = new[]
        {
            new KeyEventSpec(propagationTimePicoseconds, SpliceLossDb: 0.05, ReflectanceDb: -30.0, EventCode: "0E", Comment: "start"),
        };
        var bytes = BuildValidSorBytes(wavelengthNm, indexOfRefraction, events);

        var result = SorTraceParser.Parse(bytes);

        result.WavelengthNm.Should().BeApproximately(wavelengthNm, 1e-6);
        result.IndexOfRefraction.Should().BeApproximately(indexOfRefraction, 1e-6);
        result.KeyEvents.Should().HaveCount(1);

        var expectedDistance = propagationTimePicoseconds * 1e-12 * SpeedOfLightMetersPerSecond / indexOfRefraction / 2.0;
        result.FaultOpticalDistanceMeters.Should().BeApproximately(expectedDistance, 1e-6);
    }

    [Fact]
    public void Parse_MultipleKeyEvents_ChoosesHighestReflectanceEventNotNecessarilyTheLast()
    {
        const double indexOfRefraction = 1.4682;
        var events = new[]
        {
            new KeyEventSpec(500_000.0, 0.1, ReflectanceDb: -30.0, EventCode: "0E", Comment: "a"),
            new KeyEventSpec(1_000_000.0, 0.1, ReflectanceDb: -15.0, EventCode: "0E", Comment: "b"), // most reflective, not last.
            new KeyEventSpec(1_500_000.0, 0.1, ReflectanceDb: -50.0, EventCode: "0E", Comment: "c"),
        };
        var bytes = BuildValidSorBytes(1550.0, indexOfRefraction, events);

        var result = SorTraceParser.Parse(bytes);

        var expectedDistance = 1_000_000.0 * 1e-12 * SpeedOfLightMetersPerSecond / indexOfRefraction / 2.0;
        result.FaultOpticalDistanceMeters.Should().BeApproximately(expectedDistance, 1e-6);
    }

    [Fact]
    public void Parse_AllEventsNonReflective_FallsBackToTheLastEvent()
    {
        const double indexOfRefraction = 1.4682;
        // -1000.0 is NOT > the sentinel threshold (strict "greater than"), so all three count as
        // non-reflective and the engine must fall back to the last event in trace order.
        var events = new[]
        {
            new KeyEventSpec(500_000.0, 0.1, ReflectanceDb: -1000.0, EventCode: "0E", Comment: "a"),
            new KeyEventSpec(1_000_000.0, 0.1, ReflectanceDb: -1000.0, EventCode: "0E", Comment: "b"),
            new KeyEventSpec(1_500_000.0, 0.1, ReflectanceDb: -1000.0, EventCode: "0E", Comment: "c"),
        };
        var bytes = BuildValidSorBytes(1550.0, indexOfRefraction, events);

        var result = SorTraceParser.Parse(bytes);

        var expectedDistance = 1_500_000.0 * 1e-12 * SpeedOfLightMetersPerSecond / indexOfRefraction / 2.0;
        result.FaultOpticalDistanceMeters.Should().BeApproximately(expectedDistance, 1e-6);
    }

    // ── Malformed / structurally inconsistent input ─────────────────────────

    [Fact]
    public void Parse_BadMapSignature_ThrowsSorTraceParseException()
    {
        var bytes = BuildValidSorBytes(1550.0, 1.4682, [new KeyEventSpec(500_000.0, 0.1, -30.0, "0E", "x")]);
        bytes[0] = (byte)'X'; // corrupt "Map" -> "Xap"

        var act = () => SorTraceParser.Parse(bytes);

        act.Should().Throw<SorTraceParseException>();
    }

    [Fact]
    public void Parse_TruncatedFile_ThrowsSorTraceParseException()
    {
        var bytes = BuildValidSorBytes(1550.0, 1.4682, [new KeyEventSpec(500_000.0, 0.1, -30.0, "0E", "x")]);
        var truncated = bytes[..(bytes.Length - 5)];

        var act = () => SorTraceParser.Parse(truncated);

        act.Should().Throw<SorTraceParseException>();
    }

    [Fact]
    public void Parse_MissingFxdParamsBlock_ThrowsSorTraceParseException()
    {
        var keyEvents = BuildKeyEventsBlock([new KeyEventSpec(500_000.0, 0.1, -30.0, "0E", "x")]);
        var bytes = AssembleTrace([("KeyEvents", keyEvents)]);

        var act = () => SorTraceParser.Parse(bytes);

        act.Should().Throw<SorTraceParseException>().WithMessage("*FxdParams*");
    }

    [Fact]
    public void Parse_MissingKeyEventsBlock_ThrowsSorTraceParseException()
    {
        var fxdParams = BuildFxdParamsBlock(1550.0, 1.4682);
        var bytes = AssembleTrace([("FxdParams", fxdParams)]);

        var act = () => SorTraceParser.Parse(bytes);

        act.Should().Throw<SorTraceParseException>().WithMessage("*KeyEvents*");
    }

    [Theory]
    [InlineData((ushort)0)]
    [InlineData((ushort)65)]
    public void Parse_BlockCountOutsideSaneRange_ThrowsSorTraceParseException(ushort blockCount)
    {
        var buffer = new List<byte>();
        buffer.AddRange(Encoding.ASCII.GetBytes("Map"));
        buffer.Add(0);
        WriteUInt16BE(buffer, 1);
        WriteInt32BE(buffer, 0);
        WriteUInt16BE(buffer, blockCount); // out-of-range directory count — parser must reject
        // before attempting to read any directory entries (there may be none to read).
        var bytes = buffer.ToArray();

        var act = () => SorTraceParser.Parse(bytes);

        act.Should().Throw<SorTraceParseException>();
    }

    [Theory]
    [InlineData((ushort)0)]
    [InlineData((ushort)1001)]
    public void Parse_KeyEventsCountOutsideSaneRange_ThrowsSorTraceParseException(ushort count)
    {
        var fxdParams = BuildFxdParamsBlock(1550.0, 1.4682);
        var keyEventsCountOnly = new List<byte>();
        WriteUInt16BE(keyEventsCountOnly, count);
        var bytes = AssembleTrace([("FxdParams", fxdParams), ("KeyEvents", keyEventsCountOnly.ToArray())]);

        var act = () => SorTraceParser.Parse(bytes);

        act.Should().Throw<SorTraceParseException>();
    }

    [Fact]
    public void Parse_NullInput_ThrowsArgumentNullException()
    {
        var act = () => SorTraceParser.Parse(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Parse_CommentWithNoNullTerminatorBeforeBlockBoundary_ThrowsSorTraceParseException()
    {
        var fxdParams = BuildFxdParamsBlock(1550.0, 1.4682);

        // One event whose comment fills the full 256-byte safety bound with no terminating zero
        // byte, but the block itself is sized to exactly contain those 256 bytes (so ReadByte's
        // own end-of-block check never fires first) — this must exercise
        // ReadNullTerminatedAsciiString's own "exceeded safety bound" throw, not "unexpected end
        // of trace data".
        var keyEventsContent = new List<byte>();
        WriteUInt16BE(keyEventsContent, 1); // count
        WriteUInt16BE(keyEventsContent, 1); // eventNumber
        WriteUInt32BE(keyEventsContent, 100_000); // propagation time raw
        WriteInt16BE(keyEventsContent, 0); // slope
        WriteInt16BE(keyEventsContent, 0); // splice loss
        WriteInt32BE(keyEventsContent, 0); // reflectance
        keyEventsContent.AddRange(Encoding.ASCII.GetBytes("0E    ")); // 6-byte event code
        keyEventsContent.AddRange(Enumerable.Repeat((byte)'x', 256)); // comment, no terminator

        var bytes = AssembleTrace([("FxdParams", fxdParams), ("KeyEvents", keyEventsContent.ToArray())]);

        var act = () => SorTraceParser.Parse(bytes);

        act.Should().Throw<SorTraceParseException>();
    }
}
