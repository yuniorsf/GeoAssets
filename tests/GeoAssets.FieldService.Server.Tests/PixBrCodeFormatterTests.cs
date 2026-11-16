using Xunit;
using GeoAssets.FieldService.Server;

namespace GeoAssets.FieldService.Server.Tests;

public sealed class PixBrCodeFormatterTests
{
    [Fact]
    public void Build_EndsWithFourHexDigitCrc()
    {
        var payload = PixBrCodeFormatter.Build("11999998888", "Neutral Operator", "SAO PAULO", 123.45m, "TXN001");

        var crc = payload[^4..];
        Assert.Equal(4, crc.Length);
        Assert.True(ulong.TryParse(crc, System.Globalization.NumberStyles.HexNumber, null, out _));
        Assert.Equal(crc, crc.ToUpperInvariant());
    }

    [Fact]
    public void Build_ContainsPixGuiAndKeyAndAmount()
    {
        var payload = PixBrCodeFormatter.Build("pix@operator.com", "Neutral Operator", "SAO PAULO", 99.90m, "TXN002");

        Assert.Contains("br.gov.bcb.pix", payload, StringComparison.Ordinal);
        Assert.Contains("pix@operator.com", payload, StringComparison.Ordinal);
        Assert.Contains("99.90", payload, StringComparison.Ordinal);
        Assert.StartsWith("000201", payload, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_TruncatesMerchantNameTo25CharsAndCityTo15Chars()
    {
        var longName = new string('A', 40);
        var longCity = new string('B', 40);

        var payload = PixBrCodeFormatter.Build("11999998888", longName, longCity, 10m, "TXN003");

        Assert.Contains(new string('A', 25), payload, StringComparison.Ordinal);
        Assert.DoesNotContain(new string('A', 26), payload, StringComparison.Ordinal);
        Assert.Contains(new string('B', 15), payload, StringComparison.Ordinal);
        Assert.DoesNotContain(new string('B', 16), payload, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("", "Name", "City", 1.0, "TXN")]
    [InlineData("key", "", "City", 1.0, "TXN")]
    [InlineData("key", "Name", "", 1.0, "TXN")]
    [InlineData("key", "Name", "City", 1.0, "")]
    public void Build_MissingRequiredField_Throws(string pixKey, string name, string city, decimal amount, string txnId)
    {
        Assert.Throws<ArgumentException>(() => PixBrCodeFormatter.Build(pixKey, name, city, amount, txnId));
    }

    [Fact]
    public void Crc16Ccitt_KnownStandardInput_MatchesWellKnownTestVector()
    {
        // The canonical CRC-16/CCITT-FALSE check value for ASCII "123456789" is 0x29B1 —
        // the published test vector for this variant (poly 0x1021, init 0xFFFF, no reflect,
        // xorout 0), independent of PIX; confirms the algorithm itself, not PIX-specific framing.
        var crc = PixBrCodeFormatter.Crc16Ccitt("123456789");

        Assert.Equal(0x29B1, crc);
    }
}
