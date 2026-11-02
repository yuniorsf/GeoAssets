using GeoAssets.Core.Services;

namespace GeoAssets.FieldService.Server;

/// <summary>
/// Exactly one of <see cref="SorTraceBytes"/>/<see cref="DirectOpticalDistanceMeters"/> must be
/// set — see <c>OtdrEmergencyFaultDispatchService</c>'s own validation. The slack/pole/helix
/// parameters are caller-supplied since neither is modeled on the cable asset yet (see
/// <c>OtdrFaultSourceInput</c>'s own doc comment for why the SNMP alarm path is scope-cut the
/// same way).
/// </summary>
internal sealed record IspOtdrFaultDispatchRequest(
    string CableFeatureId,
    byte[]? SorTraceBytes,
    double? DirectOpticalDistanceMeters,
    double HelixFactor,
    double CentralOfficeSlackMeters,
    IReadOnlyList<OtdrCableSlackPoint> SlackPoints,
    IReadOnlyList<OtdrPoleAlongRoute> PolesAlongRoute,
    double SpliceMatchToleranceMeters = 1.0);
