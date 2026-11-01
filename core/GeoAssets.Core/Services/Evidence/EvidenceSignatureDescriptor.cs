using System.Globalization;

namespace GeoAssets.Core.Services.Evidence;

/// <summary>
/// The composite value written into an evidence photo's EXIF <c>ImageDescription</c> — the HMAC
/// signature plus the exact field values it was computed over, so
/// <see cref="EvidenceCaptureService.Verify"/> can recompute the same signature independently
/// from the (stripped) image bytes alone, without a side channel back to whatever system issued
/// the original capture.
/// </summary>
public sealed record EvidenceSignatureDescriptor(string Signature, double Latitude, double Longitude, DateTimeOffset CapturedAtUtc, string OrderId)
{
    private const string Prefix = "GeoAssetsEvidenceV1";

    public string ToDescriptorString() => string.Join(';', Prefix, Signature, Latitude.ToString("R", CultureInfo.InvariantCulture),
        Longitude.ToString("R", CultureInfo.InvariantCulture), CapturedAtUtc.ToString("O", CultureInfo.InvariantCulture), OrderId);

    public static bool TryParse(string? descriptor, out EvidenceSignatureDescriptor? result)
    {
        result = null;
        if (string.IsNullOrEmpty(descriptor)) return false;

        var parts = descriptor.Split(';');
        if (parts.Length != 6 || !string.Equals(parts[0], Prefix, StringComparison.Ordinal)) return false;

        if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude)) return false;
        if (!double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude)) return false;
        if (!DateTimeOffset.TryParse(parts[4], CultureInfo.InvariantCulture, DateTimeStyles.None, out var capturedAtUtc)) return false;

        result = new EvidenceSignatureDescriptor(parts[1], latitude, longitude, capturedAtUtc, parts[5]);
        return true;
    }
}
