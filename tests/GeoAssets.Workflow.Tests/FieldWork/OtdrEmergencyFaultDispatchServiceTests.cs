using System.Text;
using FluentAssertions;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Services;
using GeoAssets.Workflow.FieldWork;
using Xunit;

namespace GeoAssets.Workflow.Tests.FieldWork;

/// <summary>
/// XD01-283's dispatch-wiring ACs: exactly one of <see cref="OtdrFaultSourceInput.SorTraceBytes"/>/
/// <see cref="OtdrFaultSourceInput.DirectOpticalDistanceMeters"/> must be provided, the cable
/// feature's geometry must be a <see cref="GeoLineString"/>, and every failure path returns
/// <see cref="OtdrFaultDispatchOutcome.Failure"/> rather than throwing. The correlation algorithm
/// itself (XD01-282) is exercised by <c>OtdrFaultCorrelationEngineTests</c> in
/// <c>GeoAssets.Core.Tests</c> — this file only verifies
/// <see cref="OtdrEmergencyFaultDispatchService"/>'s own wiring and validation via a fake
/// <see cref="IOtdrFaultCorrelationEngine"/>.
/// </summary>
public class OtdrEmergencyFaultDispatchServiceTests
{
    private sealed class FakeOtdrFaultCorrelationEngine(
        OtdrFaultCorrelationResult? result = null, Exception? exceptionToThrow = null) : IOtdrFaultCorrelationEngine
    {
        public OtdrFaultCorrelationRequest? CapturedRequest { get; private set; }

        public OtdrFaultCorrelationResult Correlate(OtdrFaultCorrelationRequest request)
        {
            CapturedRequest = request;
            if (exceptionToThrow is not null)
                throw exceptionToThrow;
            return result!;
        }
    }

    private static OtdrFaultCorrelationResult CannedResult() =>
        new(FaultLocationType.AerialSpan, 123.4, new GeoPoint(-46.5, -23.55), "P1", "P2", "diagnostic summary");

    // A stand-in for the real OtdrFaultCorrelationEngine's own genuine ArgumentException (thrown
    // from its ExtractCoordinate when a cable route has fewer than 2 coordinates) — constructed
    // via a method with an actual "cableRoute" parameter so nameof(cableRoute) is a real
    // parameter reference (MA0015), without needing to reconstruct that exact NTS failure here.
    private static ArgumentException BuildCableRouteTooShortException(object? cableRoute = null) =>
        new("Cable route must have at least 2 coordinates.", nameof(cableRoute));

    private static GeoFeature LineStringCableFeature() =>
        new() { Id = "cable-1", Geometry = new GeoLineString([(-46.633, -23.55), (-46.400, -23.55)]) };

    private static GeoFeature PointCableFeature() =>
        new() { Id = "cable-1", Geometry = new GeoPoint(-46.633, -23.55) };

    private static GeoFeature NullGeometryCableFeature() =>
        new() { Id = "cable-1", Geometry = null };

    // ── Minimal self-contained .SOR trace construction (single key event) ───
    // Mirrors SorTraceParser's documented byte layout (big-endian; "Map" directory walk;
    // FxdParams/KeyEvents field offsets) — duplicated here rather than shared with
    // GeoAssets.Core.Tests so this file stays self-contained per the task's own instruction.

    private static byte[] BuildValidSorBytes(
        double propagationTimePicoseconds, double indexOfRefraction = 1.4682, double wavelengthNm = 1550.0)
    {
        var fxdParams = new byte[32];
        WriteUInt16BEAt(fxdParams, 6, (ushort)Math.Round(wavelengthNm * 10.0));
        WriteUInt32BEAt(fxdParams, 28, (uint)Math.Round(indexOfRefraction * 100_000.0));

        var keyEvents = new List<byte>();
        WriteUInt16BE(keyEvents, 1); // count
        WriteUInt16BE(keyEvents, 1); // eventNumber
        WriteUInt32BE(keyEvents, (uint)Math.Round(propagationTimePicoseconds / 10.0));
        WriteInt16BE(keyEvents, 0); // slope — unused
        WriteInt16BE(keyEvents, 100); // splice loss raw -> 0.1dB
        WriteInt32BE(keyEvents, -20_000); // reflectance raw -> -20.0dB (reflective)
        keyEvents.AddRange(Encoding.ASCII.GetBytes("0E    ")); // 6-byte event code
        keyEvents.AddRange(Encoding.ASCII.GetBytes("x"));
        keyEvents.Add(0); // null terminator

        var buffer = new List<byte>();
        buffer.AddRange(Encoding.ASCII.GetBytes("Map"));
        buffer.Add(0);
        WriteUInt16BE(buffer, 1);
        WriteInt32BE(buffer, 0);
        WriteUInt16BE(buffer, 2); // blockCount

        WriteDirectoryEntry(buffer, "FxdParams", fxdParams.Length);
        WriteDirectoryEntry(buffer, "KeyEvents", keyEvents.Count);

        buffer.AddRange(fxdParams);
        buffer.AddRange(keyEvents);
        return buffer.ToArray();
    }

    private static void WriteDirectoryEntry(List<byte> buffer, string blockId, int size)
    {
        buffer.AddRange(Encoding.ASCII.GetBytes(blockId));
        buffer.Add(0);
        WriteUInt16BE(buffer, 1);
        WriteInt32BE(buffer, size);
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

    // ── Happy paths ──────────────────────────────────────────────────────────

    [Fact]
    public void Dispatch_DirectOpticalDistancePath_SucceedsAndEngineReceivesExactDistance()
    {
        var canned = CannedResult();
        var engine = new FakeOtdrFaultCorrelationEngine(result: canned);
        var service = new OtdrEmergencyFaultDispatchService(engine);
        var source = new OtdrFaultSourceInput(SorTraceBytes: null, DirectOpticalDistanceMeters: 500.0);

        var outcome = service.Dispatch(source, LineStringCableFeature(), helixFactor: 1.0, centralOfficeSlackMeters: 0.0, slackPoints: [], polesAlongRoute: []);

        outcome.Succeeded.Should().BeTrue();
        outcome.Result.Should().BeSameAs(canned);
        engine.CapturedRequest.Should().NotBeNull();
        engine.CapturedRequest!.OpticalDistanceMeters.Should().Be(500.0);
    }

    [Fact]
    public void Dispatch_SorTracePath_Succeeds()
    {
        const double propagationTimePicoseconds = 800_000.0;
        const double indexOfRefraction = 1.4682;
        var engine = new FakeOtdrFaultCorrelationEngine(result: CannedResult());
        var service = new OtdrEmergencyFaultDispatchService(engine);
        var bytes = BuildValidSorBytes(propagationTimePicoseconds, indexOfRefraction);
        var source = new OtdrFaultSourceInput(SorTraceBytes: bytes, DirectOpticalDistanceMeters: null);

        var outcome = service.Dispatch(source, LineStringCableFeature(), helixFactor: 1.0, centralOfficeSlackMeters: 0.0, slackPoints: [], polesAlongRoute: []);

        outcome.Succeeded.Should().BeTrue();
        var expectedDistance = propagationTimePicoseconds * 1e-12 * 299_792_458.0 / indexOfRefraction / 2.0;
        engine.CapturedRequest!.OpticalDistanceMeters.Should().BeApproximately(expectedDistance, 1e-6);
    }

    // ── Source validation ────────────────────────────────────────────────────

    [Fact]
    public void Dispatch_BothSorTraceBytesAndDirectDistanceSet_ReturnsFailureNotException()
    {
        var engine = new FakeOtdrFaultCorrelationEngine(result: CannedResult());
        var service = new OtdrEmergencyFaultDispatchService(engine);
        var source = new OtdrFaultSourceInput(SorTraceBytes: BuildValidSorBytes(500_000.0), DirectOpticalDistanceMeters: 100.0);

        var outcome = service.Dispatch(source, LineStringCableFeature(), helixFactor: 1.0, centralOfficeSlackMeters: 0.0, slackPoints: [], polesAlongRoute: []);

        outcome.Succeeded.Should().BeFalse();
        outcome.FailureReason.Should().NotBeNullOrWhiteSpace();
        outcome.Result.Should().BeNull();
    }

    [Fact]
    public void Dispatch_NeitherSorTraceBytesNorDirectDistanceSet_ReturnsFailure()
    {
        var engine = new FakeOtdrFaultCorrelationEngine(result: CannedResult());
        var service = new OtdrEmergencyFaultDispatchService(engine);
        var source = new OtdrFaultSourceInput(SorTraceBytes: null, DirectOpticalDistanceMeters: null);

        var outcome = service.Dispatch(source, LineStringCableFeature(), helixFactor: 1.0, centralOfficeSlackMeters: 0.0, slackPoints: [], polesAlongRoute: []);

        outcome.Succeeded.Should().BeFalse();
        outcome.FailureReason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Dispatch_MalformedSorBytes_ReturnsFailureWithParserDiagnosticMessage()
    {
        var engine = new FakeOtdrFaultCorrelationEngine(result: CannedResult());
        var service = new OtdrEmergencyFaultDispatchService(engine);
        var source = new OtdrFaultSourceInput(SorTraceBytes: new byte[16], DirectOpticalDistanceMeters: null);

        var outcome = service.Dispatch(source, LineStringCableFeature(), helixFactor: 1.0, centralOfficeSlackMeters: 0.0, slackPoints: [], polesAlongRoute: []);

        outcome.Succeeded.Should().BeFalse();
        outcome.FailureReason.Should().Contain("Map");
    }

    // ── Cable geometry validation ────────────────────────────────────────────

    [Fact]
    public void Dispatch_CableFeatureGeometryIsNull_ReturnsFailure()
    {
        var engine = new FakeOtdrFaultCorrelationEngine(result: CannedResult());
        var service = new OtdrEmergencyFaultDispatchService(engine);
        var source = new OtdrFaultSourceInput(SorTraceBytes: null, DirectOpticalDistanceMeters: 100.0);

        var outcome = service.Dispatch(source, NullGeometryCableFeature(), helixFactor: 1.0, centralOfficeSlackMeters: 0.0, slackPoints: [], polesAlongRoute: []);

        outcome.Succeeded.Should().BeFalse();
        outcome.FailureReason.Should().Contain("line-string");
    }

    [Fact]
    public void Dispatch_CableFeatureGeometryIsNotLineString_ReturnsFailure()
    {
        var engine = new FakeOtdrFaultCorrelationEngine(result: CannedResult());
        var service = new OtdrEmergencyFaultDispatchService(engine);
        var source = new OtdrFaultSourceInput(SorTraceBytes: null, DirectOpticalDistanceMeters: 100.0);

        var outcome = service.Dispatch(source, PointCableFeature(), helixFactor: 1.0, centralOfficeSlackMeters: 0.0, slackPoints: [], polesAlongRoute: []);

        outcome.Succeeded.Should().BeFalse();
        outcome.FailureReason.Should().Contain("line-string");
    }

    // ── Engine failure surfacing ─────────────────────────────────────────────

    [Fact]
    public void Dispatch_EngineThrowsArgumentException_ReturnsFailureWithExceptionMessage()
    {
        var engine = new FakeOtdrFaultCorrelationEngine(exceptionToThrow: BuildCableRouteTooShortException());
        var service = new OtdrEmergencyFaultDispatchService(engine);
        var source = new OtdrFaultSourceInput(SorTraceBytes: null, DirectOpticalDistanceMeters: 100.0);

        var outcome = service.Dispatch(source, LineStringCableFeature(), helixFactor: 1.0, centralOfficeSlackMeters: 0.0, slackPoints: [], polesAlongRoute: []);

        outcome.Succeeded.Should().BeFalse();
        outcome.FailureReason.Should().Contain("at least 2 coordinates");
    }

    // ── Null-argument validation ─────────────────────────────────────────────

    [Fact]
    public void Dispatch_NullSource_ThrowsArgumentNullException()
    {
        var service = new OtdrEmergencyFaultDispatchService(new FakeOtdrFaultCorrelationEngine(result: CannedResult()));

        var act = () => service.Dispatch(null!, LineStringCableFeature(), helixFactor: 1.0, centralOfficeSlackMeters: 0.0, slackPoints: [], polesAlongRoute: []);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Dispatch_NullCableFeature_ThrowsArgumentNullException()
    {
        var service = new OtdrEmergencyFaultDispatchService(new FakeOtdrFaultCorrelationEngine(result: CannedResult()));
        var source = new OtdrFaultSourceInput(SorTraceBytes: null, DirectOpticalDistanceMeters: 100.0);

        var act = () => service.Dispatch(source, null!, helixFactor: 1.0, centralOfficeSlackMeters: 0.0, slackPoints: [], polesAlongRoute: []);

        act.Should().Throw<ArgumentNullException>();
    }
}
