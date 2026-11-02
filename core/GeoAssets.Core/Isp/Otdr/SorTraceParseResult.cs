namespace GeoAssets.Core.Isp.Otdr;

/// <summary>Result of <see cref="SorTraceParser.Parse"/> (XD01-283).</summary>
public sealed record SorTraceParseResult(
    double IndexOfRefraction,
    double WavelengthNm,
    IReadOnlyList<SorKeyEvent> KeyEvents,
    double FaultOpticalDistanceMeters);
