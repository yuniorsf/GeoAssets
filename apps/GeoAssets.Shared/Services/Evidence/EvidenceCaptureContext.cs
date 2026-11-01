namespace GeoAssets.Shared.Services.Evidence;

/// <summary>
/// The non-GPS identifying context burned into an evidence photo's watermark and bound into its
/// HMAC signature (XD01-244) — a field-work order, the technician capturing it, and the asset the
/// photo documents. <paramref name="CompassHeadingDegrees"/> is the azimuth at capture time
/// (0-360, null if the device has no compass/magnetometer).
/// </summary>
public sealed record EvidenceCaptureContext(
    string OrderId,
    string TechnicianId,
    string AssetTag,
    DateTimeOffset CapturedAtUtc,
    double? CompassHeadingDegrees);
