using System.Text;

namespace GeoAssets.Shared.Services.Evidence;

/// <summary>
/// Writes and reads a single EXIF <c>ImageDescription</c> tag (0x010E) on a JPEG, as a minimal
/// hand-rolled APP1/Exif segment rather than a full third-party EXIF library — XD01-244 only
/// needs this one ASCII tag (carrying the HMAC signature + signed metadata), and a bounded,
/// fully self-tested binary format avoids pulling in a general-purpose EXIF dependency (and the
/// library-choice/licensing decision that would come with it) for a one-field need.
///
/// The segment is always inserted immediately after the SOI marker (and after an existing
/// APP0/JFIF segment, if present) — the conventional Exif position, and the same position
/// <see cref="StripImageDescriptionSegment"/> looks for it at, so insertion and stripping are
/// exact inverses of each other (round-trip relied on by <see cref="EvidenceCaptureService"/>'s
/// tamper-evident signing scheme).
/// </summary>
public static class JpegExifImageDescriptionCodec
{
    private static readonly byte[] ExifSignature = "Exif\0\0"u8.ToArray();
    private const ushort ImageDescriptionTag = 0x010E;
    private const ushort AsciiType = 2;

    public static byte[] WriteImageDescription(byte[] jpegBytes, string description)
    {
        ArgumentNullException.ThrowIfNull(jpegBytes);
        ArgumentNullException.ThrowIfNull(description);
        EnsureSoi(jpegBytes);

        var insertionIndex = FindSegmentInsertionIndex(jpegBytes);
        var segment = BuildExifSegment(description);

        var result = new byte[jpegBytes.Length + segment.Length];
        Array.Copy(jpegBytes, 0, result, 0, insertionIndex);
        Array.Copy(segment, 0, result, insertionIndex, segment.Length);
        Array.Copy(jpegBytes, insertionIndex, result, insertionIndex + segment.Length, jpegBytes.Length - insertionIndex);
        return result;
    }

    /// <summary>
    /// Removes the Exif APP1 segment <see cref="WriteImageDescription"/> would insert, recovering
    /// the original pre-signature bytes. A no-op (returns <paramref name="jpegBytes"/> unchanged)
    /// if no such segment is present at the expected position.
    /// </summary>
    public static byte[] StripImageDescriptionSegment(byte[] jpegBytes)
    {
        ArgumentNullException.ThrowIfNull(jpegBytes);
        EnsureSoi(jpegBytes);

        var index = FindSegmentInsertionIndex(jpegBytes);
        if (!TryReadSegmentAt(jpegBytes, index, out var segmentLength, out var isExif) || !isExif)
            return jpegBytes;

        var result = new byte[jpegBytes.Length - segmentLength];
        Array.Copy(jpegBytes, 0, result, 0, index);
        Array.Copy(jpegBytes, index + segmentLength, result, index, jpegBytes.Length - index - segmentLength);
        return result;
    }

    public static bool TryReadImageDescription(byte[] jpegBytes, out string? description)
    {
        ArgumentNullException.ThrowIfNull(jpegBytes);
        description = null;
        EnsureSoi(jpegBytes);

        var index = FindSegmentInsertionIndex(jpegBytes);
        if (!TryReadSegmentAt(jpegBytes, index, out var segmentLength, out var isExif) || !isExif)
            return false;

        var payload = jpegBytes.AsSpan(index + 4, segmentLength - 4); // skip marker(2) + length(2)
        var tiff = payload[ExifSignature.Length..];

        var byteOrder = tiff[..2];
        var littleEndian = byteOrder[0] == (byte)'I';
        var ifd0Offset = ReadUInt32(tiff[4..8], littleEndian);

        var ifd = tiff[(int)ifd0Offset..];
        var entryCount = ReadUInt16(ifd[..2], littleEndian);
        for (var i = 0; i < entryCount; i++)
        {
            var entry = ifd.Slice(2 + (i * 12), 12);
            var tag = ReadUInt16(entry[..2], littleEndian);
            if (tag != ImageDescriptionTag) continue;

            var count = (int)ReadUInt32(entry[4..8], littleEndian);
            var valueField = entry[8..12];

            var stringBytes = count <= 4
                ? valueField[..count]
                : tiff.Slice((int)ReadUInt32(valueField, littleEndian), count);

            var nullTerminatorIndex = stringBytes.IndexOf((byte)0);
            var textLength = nullTerminatorIndex >= 0 ? nullTerminatorIndex : stringBytes.Length;
            description = Encoding.ASCII.GetString(stringBytes[..textLength]);
            return true;
        }

        return false;
    }

    private static void EnsureSoi(byte[] jpegBytes)
    {
        if (jpegBytes.Length < 2 || jpegBytes[0] != 0xFF || jpegBytes[1] != 0xD8)
            throw new ArgumentException("Not a valid JPEG (missing SOI marker).", nameof(jpegBytes));
    }

    /// <summary>The conventional Exif position: right after SOI, and after APP0/JFIF if present.</summary>
    private static int FindSegmentInsertionIndex(byte[] jpegBytes)
    {
        var pos = 2;
        while (pos + 4 <= jpegBytes.Length && jpegBytes[pos] == 0xFF && jpegBytes[pos + 1] == 0xE0)
        {
            var length = ReadUInt16(jpegBytes.AsSpan(pos + 2, 2), littleEndian: false);
            pos += 2 + length;
        }
        return pos;
    }

    private static bool TryReadSegmentAt(byte[] jpegBytes, int index, out int segmentLength, out bool isExif)
    {
        segmentLength = 0;
        isExif = false;
        if (index + 4 > jpegBytes.Length || jpegBytes[index] != 0xFF || jpegBytes[index + 1] != 0xE1)
            return false;

        segmentLength = 2 + ReadUInt16(jpegBytes.AsSpan(index + 2, 2), littleEndian: false);
        if (index + segmentLength > jpegBytes.Length)
            return false;

        var payload = jpegBytes.AsSpan(index + 4, Math.Min(ExifSignature.Length, segmentLength - 4));
        isExif = payload.SequenceEqual(ExifSignature);
        return true;
    }

    private static byte[] BuildExifSegment(string description)
    {
        var stringBytes = Encoding.ASCII.GetBytes(description + "\0");
        const int tiffStart = ExifSignatureAndPaddingLength;
        const int ifd0Offset = 8; // relative to tiffStart
        const int entryCount = 1;
        const int ifdBytesBeforeStringArea = 2 + (entryCount * 12) + 4; // count + entries + next-IFD offset
        var inlineValue = stringBytes.Length <= 4;
        var stringAreaOffset = ifd0Offset + ifdBytesBeforeStringArea; // relative to tiffStart

        var tiffLength = ifd0Offset + ifdBytesBeforeStringArea + (inlineValue ? 0 : stringBytes.Length);
        var payload = new byte[tiffStart + tiffLength];

        ExifSignature.CopyTo(payload, 0);

        // TIFF header: little-endian "II", magic 42, IFD0 offset.
        payload[tiffStart] = (byte)'I';
        payload[tiffStart + 1] = (byte)'I';
        WriteUInt16(payload.AsSpan(tiffStart + 2, 2), 42, littleEndian: true);
        WriteUInt32(payload.AsSpan(tiffStart + 4, 4), ifd0Offset, littleEndian: true);

        var ifd = payload.AsSpan(tiffStart + ifd0Offset);
        WriteUInt16(ifd[..2], entryCount, littleEndian: true);

        var entry = ifd.Slice(2, 12);
        WriteUInt16(entry[..2], ImageDescriptionTag, littleEndian: true);
        WriteUInt16(entry[2..4], AsciiType, littleEndian: true);
        WriteUInt32(entry[4..8], (uint)stringBytes.Length, littleEndian: true);
        if (inlineValue)
            stringBytes.CopyTo(entry[8..(8 + stringBytes.Length)]);
        else
            WriteUInt32(entry[8..12], (uint)stringAreaOffset, littleEndian: true);

        WriteUInt32(ifd.Slice(2 + 12, 4), 0, littleEndian: true); // next IFD offset

        if (!inlineValue)
            stringBytes.CopyTo(payload.AsSpan(tiffStart + stringAreaOffset));

        var segment = new byte[4 + payload.Length];
        segment[0] = 0xFF;
        segment[1] = 0xE1;
        WriteUInt16(segment.AsSpan(2, 2), (ushort)(2 + payload.Length), littleEndian: false);
        payload.CopyTo(segment, 4);
        return segment;
    }

    private const int ExifSignatureAndPaddingLength = 6; // "Exif" + 2 padding bytes

    private static ushort ReadUInt16(ReadOnlySpan<byte> bytes, bool littleEndian) =>
        littleEndian
            ? (ushort)(bytes[0] | (bytes[1] << 8))
            : (ushort)((bytes[0] << 8) | bytes[1]);

    private static uint ReadUInt32(ReadOnlySpan<byte> bytes, bool littleEndian) =>
        littleEndian
            ? (uint)(bytes[0] | (bytes[1] << 8) | (bytes[2] << 16) | (bytes[3] << 24))
            : (uint)((bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3]);

    private static void WriteUInt16(Span<byte> destination, ushort value, bool littleEndian)
    {
        if (littleEndian) { destination[0] = (byte)value; destination[1] = (byte)(value >> 8); }
        else { destination[0] = (byte)(value >> 8); destination[1] = (byte)value; }
    }

    private static void WriteUInt32(Span<byte> destination, uint value, bool littleEndian)
    {
        if (littleEndian)
        {
            destination[0] = (byte)value;
            destination[1] = (byte)(value >> 8);
            destination[2] = (byte)(value >> 16);
            destination[3] = (byte)(value >> 24);
        }
        else
        {
            destination[0] = (byte)(value >> 24);
            destination[1] = (byte)(value >> 16);
            destination[2] = (byte)(value >> 8);
            destination[3] = (byte)value;
        }
    }
}
