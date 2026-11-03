namespace GeoAssets.Core.Isp.NeutralOperator.Evidence;

/// <summary>
/// The five fields XD01-261 requires every sealed field photo to carry, in both the EXIF profile
/// and the burned-in pixel watermark.
/// </summary>
public sealed record FieldPhotoEvidenceMetadata(
    string InspectorId,
    string PoleId,
    DateTimeOffset UtcTimestamp,
    double Latitude,
    double Longitude,
    double CompassHeadingDegrees,
    string OrderId);
