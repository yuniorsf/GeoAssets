using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace GeoAssets.Core.Isp.NeutralOperator.Evidence;

/// <summary>
/// Computes and verifies the on-device HMAC-SHA256 seal over a field photo: a device-side
/// tamper-evidence digest, independent of the EXIF/watermark burn-in done by
/// <see cref="FieldPhotoWatermarker"/>. Mirrors the HMACSHA256.HashData(key, data) -&gt;
/// Convert.ToBase64String(hash) convention already used by
/// GeoAssets.Workflow.Contractors.EspelhoDeMedicaoDossierGenerator.
/// </summary>
public static class FieldPhotoEvidenceSealer
{
    /// <summary>
    /// Digest input is exactly RawImageBytes || Lat || Lon || UtcTimestamp || OrderId, per XD01-261's
    /// literal formula — deliberately NOT InspectorId/PoleId/CompassHeadingDegrees, which are
    /// EXIF/watermark concerns only, not part of the signed digest.
    /// </summary>
    public static string ComputeDigest(byte[] rawImageBytes, FieldPhotoEvidenceMetadata metadata, string deviceSecret)
    {
        ArgumentNullException.ThrowIfNull(rawImageBytes);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentException.ThrowIfNullOrEmpty(deviceSecret);

        var data = BuildDigestInput(rawImageBytes, metadata);
        var key = Encoding.UTF8.GetBytes(deviceSecret);
        var hash = HMACSHA256.HashData(key, data);
        return Convert.ToBase64String(hash);
    }

    public static bool VerifyDigest(
        byte[] receivedImageBytes,
        FieldPhotoEvidenceMetadata metadata,
        string claimedDigestBase64,
        string deviceSecret)
    {
        ArgumentNullException.ThrowIfNull(receivedImageBytes);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentException.ThrowIfNullOrEmpty(deviceSecret);

        if (string.IsNullOrEmpty(claimedDigestBase64))
            return false;

        var recomputed = ComputeDigest(receivedImageBytes, metadata, deviceSecret);

        var recomputedBytes = Convert.FromBase64String(recomputed);
        byte[] claimedBytes;
        try
        {
            claimedBytes = Convert.FromBase64String(claimedDigestBase64);
        }
        catch (FormatException)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(recomputedBytes, claimedBytes);
    }

    private static byte[] BuildDigestInput(byte[] rawImageBytes, FieldPhotoEvidenceMetadata metadata)
    {
        var latBytes = Encoding.UTF8.GetBytes(metadata.Latitude.ToString("R", CultureInfo.InvariantCulture));
        var lonBytes = Encoding.UTF8.GetBytes(metadata.Longitude.ToString("R", CultureInfo.InvariantCulture));
        var timestampBytes = Encoding.UTF8.GetBytes(metadata.UtcTimestamp.ToString("O", CultureInfo.InvariantCulture));
        var orderIdBytes = Encoding.UTF8.GetBytes(metadata.OrderId);

        var buffer = new byte[rawImageBytes.Length + latBytes.Length + lonBytes.Length + timestampBytes.Length + orderIdBytes.Length];
        var offset = 0;

        Buffer.BlockCopy(rawImageBytes, 0, buffer, offset, rawImageBytes.Length);
        offset += rawImageBytes.Length;

        Buffer.BlockCopy(latBytes, 0, buffer, offset, latBytes.Length);
        offset += latBytes.Length;

        Buffer.BlockCopy(lonBytes, 0, buffer, offset, lonBytes.Length);
        offset += lonBytes.Length;

        Buffer.BlockCopy(timestampBytes, 0, buffer, offset, timestampBytes.Length);
        offset += timestampBytes.Length;

        Buffer.BlockCopy(orderIdBytes, 0, buffer, offset, orderIdBytes.Length);

        return buffer;
    }
}
