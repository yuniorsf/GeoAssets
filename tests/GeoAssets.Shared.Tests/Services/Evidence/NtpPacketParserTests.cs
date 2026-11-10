using FluentAssertions;
using GeoAssets.Shared.Services.Evidence;
using Xunit;

namespace GeoAssets.Shared.Tests.Services.Evidence;

public class NtpPacketParserTests
{
    private static readonly DateTime NtpEpoch = new(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ParseTransmitTimestamp_WholeSecondsOnly_RoundTripsExactUtcInstant()
    {
        var target = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var packet = BuildPacket((uint)(target - NtpEpoch).TotalSeconds, fraction: 0);

        var result = NtpPacketParser.ParseTransmitTimestamp(packet);

        result.Should().Be(new DateTimeOffset(target, TimeSpan.Zero));
    }

    [Fact]
    public void ParseTransmitTimestamp_HalfSecondFraction_AddsFiveHundredMilliseconds()
    {
        var target = new DateTime(2026, 6, 15, 12, 0, 0, DateTimeKind.Utc);
        var packet = BuildPacket((uint)(target - NtpEpoch).TotalSeconds, fraction: 0x8000_0000U);

        var result = NtpPacketParser.ParseTransmitTimestamp(packet);

        result.Should().Be(new DateTimeOffset(target.AddMilliseconds(500), TimeSpan.Zero));
    }

    [Fact]
    public void ParseTransmitTimestamp_ResultIsAlwaysUtc()
    {
        var packet = BuildPacket((uint)(new DateTime(2026, 11, 10, 8, 0, 0, DateTimeKind.Utc) - NtpEpoch).TotalSeconds, fraction: 0);

        var result = NtpPacketParser.ParseTransmitTimestamp(packet);

        result.Offset.Should().Be(TimeSpan.Zero);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(47)]
    public void ParseTransmitTimestamp_PacketShorterThanMinimumLength_Throws(int length)
    {
        var tooShort = new byte[length];

        var act = () => NtpPacketParser.ParseTransmitTimestamp(tooShort);

        act.Should().Throw<ArgumentException>();
    }

    private static byte[] BuildPacket(uint wholeSeconds, uint fraction)
    {
        var packet = new byte[48];
        WriteBigEndianUInt32(packet, 40, wholeSeconds);
        WriteBigEndianUInt32(packet, 44, fraction);
        return packet;
    }

    private static void WriteBigEndianUInt32(byte[] buffer, int offset, uint value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }
}
