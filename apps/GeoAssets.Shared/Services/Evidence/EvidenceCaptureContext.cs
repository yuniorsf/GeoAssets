namespace GeoAssets.Shared.Services.Evidence;

/// <summary>
/// The non-GPS identifying context burned into an evidence photo's watermark and bound into its
/// HMAC signature (XD01-244) — a field-work order, the technician capturing it, and the asset the
/// photo documents. <paramref name="CompassHeadingDegrees"/> is the azimuth at capture time
/// (0-360, null if the device has no compass/magnetometer). <paramref name="CapturedAtUtc"/> is
/// overwritten by the capture gateway with a trusted (NTP) timestamp when available (XD01-235) —
/// callers should not rely on the value they pass in surviving to the signed result.
/// <paramref name="TiltDegrees"/> is the device's inclination from horizontal at capture time
/// (0 = lying flat, 90 = upright; null if the device has no accelerometer) (XD01-235).
/// </summary>
public sealed record EvidenceCaptureContext(
    string OrderId,
    string TechnicianId,
    string AssetTag,
    DateTimeOffset CapturedAtUtc,
    double? CompassHeadingDegrees,
    double? TiltDegrees = null);
