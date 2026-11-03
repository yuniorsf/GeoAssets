namespace GeoAssets.Core.Isp.NeutralOperator.Evidence;

/// <summary>
/// The five fields as read back out of a watermarked photo's EXIF profile, for verification
/// (tests, or a future audit tool) that sealing actually embedded them.
/// </summary>
public sealed record FieldPhotoWatermarkFields(
    string InspectorId,
    string PoleId,
    DateTimeOffset UtcTimestamp,
    double Latitude,
    double Longitude,
    double CompassHeadingDegrees);
