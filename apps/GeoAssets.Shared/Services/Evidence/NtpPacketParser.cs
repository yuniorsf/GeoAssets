namespace GeoAssets.Shared.Services.Evidence;

/// <summary>
/// Pure parsing of an SNTP/NTP server's 48-byte response packet (RFC 5905 §7.3) — split out from
/// the actual UDP socket I/O so XD01-235's trusted-timestamp parsing logic is unit-testable
/// without a network call. Only the Transmit Timestamp field (offset 40, a 64-bit NTP short
/// format: 32-bit whole seconds since the NTP epoch + 32-bit binary fraction of a second) is read.
/// </summary>
public static class NtpPacketParser
{
    private const int TransmitTimestampOffset = 40;
    private const int MinimumPacketLength = 48;
    private static readonly DateTime NtpEpoch = new(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public static DateTimeOffset ParseTransmitTimestamp(ReadOnlySpan<byte> response)
    {
        if (response.Length < MinimumPacketLength)
            throw new ArgumentException($"NTP response must be at least {MinimumPacketLength} bytes; got {response.Length}.", nameof(response));

        var wholeSeconds = ReadBigEndianUInt32(response, TransmitTimestampOffset);
        var fraction = ReadBigEndianUInt32(response, TransmitTimestampOffset + 4);

        var milliseconds = wholeSeconds * 1000.0 + fraction * (1000.0 / 0x1_0000_0000UL);
        return new DateTimeOffset(NtpEpoch.AddMilliseconds(milliseconds), TimeSpan.Zero);
    }

    private static uint ReadBigEndianUInt32(ReadOnlySpan<byte> buffer, int offset) =>
        ((uint)buffer[offset] << 24) | ((uint)buffer[offset + 1] << 16) | ((uint)buffer[offset + 2] << 8) | buffer[offset + 3];
}
