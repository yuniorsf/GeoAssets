namespace GeoAssets.Core.Isp.Otdr;

/// <summary>One parsed Telcordia SR-4731 "KeyEvents" block record (XD01-283).</summary>
public sealed record SorKeyEvent(
    int EventNumber,
    double PropagationTimePicoseconds,
    double SpliceLossDb,
    double ReflectanceDb,
    string EventCode,
    string Comment);
