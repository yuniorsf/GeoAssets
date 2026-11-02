using GeoAssets.Core.Models.Geometry;

namespace GeoAssets.Core.Services;

/// <summary>
/// Input to <see cref="IOtdrFaultCorrelationEngine.Correlate"/> (XD01-282).
/// <see cref="OpticalDistanceMeters"/> is the raw OTDR-measured distance along the fiber core;
/// <see cref="HelixFactor"/> converts it to physical sheath length (sheath length = optical
/// distance / helix factor — the fiber inside the cable jacket is longer than the jacket itself
/// due to its helical lay). <see cref="CentralOfficeSlackMeters"/> is the coiled fiber stored at
/// the head end before the cable leaves the building. <see cref="CableRoute"/> is the cable's own
/// WGS84 geometry (the same CRS every <see cref="GeoPoint"/>/<see cref="GeoLineString"/> in this
/// codebase stores) — reprojected to its SIRGAS 2000 UTM zone internally for planar-distance
/// correctness, never accepted pre-projected.
/// </summary>
public sealed record OtdrFaultCorrelationRequest(
    double OpticalDistanceMeters,
    double HelixFactor,
    double CentralOfficeSlackMeters,
    IReadOnlyList<OtdrCableSlackPoint> SlackPoints,
    GeoLineString CableRoute,
    IReadOnlyList<OtdrPoleAlongRoute> PolesAlongRoute,
    double SpliceMatchToleranceMeters = 1.0);
