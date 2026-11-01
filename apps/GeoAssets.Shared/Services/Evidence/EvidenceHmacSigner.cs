using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace GeoAssets.Shared.Services.Evidence;

/// <summary>
/// XD01-244's "HMAC-SHA256 signature over image bytes + lat/lon/date/order-id" acceptance
/// criterion, computed literally: the image bytes (with no EXIF signature segment of our own yet
/// — see <see cref="JpegExifImageDescriptionCodec.StripImageDescriptionSegment"/>) concatenated
/// with a canonical, culture-invariant rendering of the four signed fields, HMAC-SHA256'd with
/// the tenant secret as key. Any later byte change to the image, or to any signed field, changes
/// the input and therefore the signature — which is what makes re-verification tamper-evident.
/// </summary>
public static class EvidenceHmacSigner
{
    public static string ComputeSignature(
        byte[] imageBytes, double latitude, double longitude, DateTimeOffset capturedAtUtc, string orderId, string tenantSecret)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);
        ArgumentException.ThrowIfNullOrEmpty(orderId);
        ArgumentException.ThrowIfNullOrEmpty(tenantSecret);

        var metadata = CanonicalMetadata(latitude, longitude, capturedAtUtc, orderId);
        var input = new byte[imageBytes.Length + metadata.Length];
        imageBytes.CopyTo(input, 0);
        metadata.CopyTo(input, imageBytes.Length);

        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(tenantSecret), input);
        return Convert.ToBase64String(hash);
    }

    private static byte[] CanonicalMetadata(double latitude, double longitude, DateTimeOffset capturedAtUtc, string orderId) =>
        Encoding.UTF8.GetBytes(string.Join('|',
            latitude.ToString("R", CultureInfo.InvariantCulture),
            longitude.ToString("R", CultureInfo.InvariantCulture),
            capturedAtUtc.ToString("O", CultureInfo.InvariantCulture),
            orderId));
}
