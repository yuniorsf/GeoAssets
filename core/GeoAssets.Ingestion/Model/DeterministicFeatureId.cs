using System.Security.Cryptography;
using System.Text;

namespace GeoAssets.Ingestion.Model;

/// <summary>
/// Stable feature id of a source element: an RFC 9562 version-8 UUID built from the SHA-256 of
/// <see cref="SourceKey.ToString"/>. The same element always maps to the same id, so re-imports are upserts and
/// edges can name targets that have not been written yet.
/// </summary>
public static class DeterministicFeatureId
{
    public static string For(SourceKey key)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(key.ToString()), hash);
        Span<byte> bytes = hash[..16];
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x80); // version 8
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80); // RFC 9562 variant
        return new Guid(bytes, bigEndian: true).ToString();
    }
}
