using System.Globalization;
using ImageMagick;
using ImageMagick.Drawing;

namespace GeoAssets.Core.Isp.NeutralOperator.Evidence;

/// <summary>
/// Burns the five required fields into both the image's EXIF profile and its visible pixels
/// ("pixel-burned watermark" per XD01-261). Uses Magick.NET (Apache 2.0, no commercial license
/// required) — SixLabors.ImageSharp/.Drawing were evaluated first but both now require a paid
/// Six Labors license even for a Debug build under this repo's /warnaserror gate, so they were
/// rejected rather than adding an unapproved licensing cost.
/// </summary>
public static class FieldPhotoWatermarker
{
    private const string InspectorIdPrefix = "InspectorId=";
    private const string PoleIdPrefix = "PoleId=";
    private const string FieldSeparator = ";";

    public static byte[] BurnWatermark(byte[] rawImageBytes, FieldPhotoEvidenceMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(rawImageBytes);
        ArgumentNullException.ThrowIfNull(metadata);

        using var image = new MagickImage(rawImageBytes);

        var profile = new ExifProfile();
        profile.SetValue(ExifTag.GPSLatitudeRef, metadata.Latitude >= 0 ? "N" : "S");
        profile.SetValue(ExifTag.GPSLatitude, ToDegreesMinutesSeconds(Math.Abs(metadata.Latitude)));
        profile.SetValue(ExifTag.GPSLongitudeRef, metadata.Longitude >= 0 ? "E" : "W");
        profile.SetValue(ExifTag.GPSLongitude, ToDegreesMinutesSeconds(Math.Abs(metadata.Longitude)));
        profile.SetValue(ExifTag.GPSImgDirectionRef, "T");
        profile.SetValue(ExifTag.GPSImgDirection, new Rational(NormalizeHeading(metadata.CompassHeadingDegrees)));
        profile.SetValue(ExifTag.DateTimeOriginal, metadata.UtcTimestamp.UtcDateTime.ToString("yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture));
        profile.SetValue(ExifTag.ImageDescription, $"{InspectorIdPrefix}{metadata.InspectorId}{FieldSeparator}{PoleIdPrefix}{metadata.PoleId}");
        image.SetProfile(profile);

        var watermarkText = BuildWatermarkText(metadata);
        new Drawables()
            .FontPointSize(18)
            .FillColor(MagickColors.White)
            .StrokeColor(MagickColors.Black)
            .StrokeWidth(1)
            .TextAlignment(TextAlignment.Left)
            .Gravity(Gravity.Southwest)
            .Text(10, 10, watermarkText)
            .Draw(image);

        return image.ToByteArray();
    }

    public static FieldPhotoWatermarkFields ReadExifFields(byte[] watermarkedImageBytes)
    {
        ArgumentNullException.ThrowIfNull(watermarkedImageBytes);

        using var image = new MagickImage(watermarkedImageBytes);
        var profile = image.GetExifProfile()
            ?? throw new InvalidOperationException("Watermarked image has no EXIF profile.");

        var latRef = profile.GetValue(ExifTag.GPSLatitudeRef)?.Value ?? "N";
        var lat = FromDegreesMinutesSeconds(profile.GetValue(ExifTag.GPSLatitude)!.Value);
        if (string.Equals(latRef, "S", StringComparison.OrdinalIgnoreCase))
            lat = -lat;

        var lonRef = profile.GetValue(ExifTag.GPSLongitudeRef)?.Value ?? "E";
        var lon = FromDegreesMinutesSeconds(profile.GetValue(ExifTag.GPSLongitude)!.Value);
        if (string.Equals(lonRef, "W", StringComparison.OrdinalIgnoreCase))
            lon = -lon;

        var heading = profile.GetValue(ExifTag.GPSImgDirection)!.Value.ToDouble();

        var timestamp = DateTime.ParseExact(
            profile.GetValue(ExifTag.DateTimeOriginal)!.Value,
            "yyyy:MM:dd HH:mm:ss",
            CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal);

        var description = profile.GetValue(ExifTag.ImageDescription)?.Value ?? string.Empty;
        var (inspectorId, poleId) = ParseDescription(description);

        return new FieldPhotoWatermarkFields(inspectorId, poleId, new DateTimeOffset(timestamp, TimeSpan.Zero), lat, lon, heading);
    }

    private static string BuildWatermarkText(FieldPhotoEvidenceMetadata metadata) =>
        $"Inspector:{metadata.InspectorId} Pole:{metadata.PoleId} " +
        $"{metadata.UtcTimestamp.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)} " +
        $"Lat:{metadata.Latitude.ToString("F6", CultureInfo.InvariantCulture)} " +
        $"Lon:{metadata.Longitude.ToString("F6", CultureInfo.InvariantCulture)} " +
        $"Hdg:{metadata.CompassHeadingDegrees.ToString("F1", CultureInfo.InvariantCulture)}";

    private static Rational[] ToDegreesMinutesSeconds(double decimalDegrees)
    {
        var degrees = Math.Floor(decimalDegrees);
        var minutesTotal = (decimalDegrees - degrees) * 60;
        var minutes = Math.Floor(minutesTotal);
        var seconds = (minutesTotal - minutes) * 60;

        return
        [
            new Rational(degrees),
            new Rational(minutes),
            new Rational(seconds),
        ];
    }

    private static double FromDegreesMinutesSeconds(Rational[] dms) =>
        dms[0].ToDouble() + dms[1].ToDouble() / 60.0 + dms[2].ToDouble() / 3600.0;

    private static double NormalizeHeading(double headingDegrees)
    {
        var normalized = headingDegrees % 360.0;
        return normalized < 0 ? normalized + 360.0 : normalized;
    }

    private static (string InspectorId, string PoleId) ParseDescription(string description)
    {
        var parts = description.Split(FieldSeparator, StringSplitOptions.None);
        var inspectorId = parts.Length > 0 && parts[0].StartsWith(InspectorIdPrefix, StringComparison.Ordinal)
            ? parts[0][InspectorIdPrefix.Length..]
            : string.Empty;
        var poleId = parts.Length > 1 && parts[1].StartsWith(PoleIdPrefix, StringComparison.Ordinal)
            ? parts[1][PoleIdPrefix.Length..]
            : string.Empty;
        return (inspectorId, poleId);
    }
}
