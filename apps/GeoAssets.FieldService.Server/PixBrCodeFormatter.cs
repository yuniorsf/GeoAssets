using System.Globalization;
using System.Text;

namespace GeoAssets.FieldService.Server;

/// <summary>
/// Pure builder for a static PIX "BR Code" ("Copia e Cola") payload — the EMV-QR-derived format
/// Banco Central do Brasil's PIX manual specifies. Unlike NF Com or Boleto Registrado, this is
/// fully generatable offline from a PIX key with no bank API round-trip: every field below is
/// the real, publicly documented wire format, not a GeoAssets placeholder.
/// </summary>
internal static class PixBrCodeFormatter
{
    public static string Build(string pixKey, string merchantName, string merchantCity, decimal amount, string transactionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pixKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(merchantName);
        ArgumentException.ThrowIfNullOrWhiteSpace(merchantCity);
        ArgumentException.ThrowIfNullOrWhiteSpace(transactionId);

        var merchantAccountInfo = Tlv("00", "br.gov.bcb.pix") + Tlv("01", pixKey);
        var additionalData = Tlv("05", Truncate(transactionId, 25));

        var sb = new StringBuilder();
        sb.Append(Tlv("00", "01"));
        sb.Append(Tlv("26", merchantAccountInfo));
        sb.Append(Tlv("52", "0000"));
        sb.Append(Tlv("53", "986"));
        sb.Append(Tlv("54", amount.ToString("F2", CultureInfo.InvariantCulture)));
        sb.Append(Tlv("58", "BR"));
        sb.Append(Tlv("59", Truncate(merchantName, 25)));
        sb.Append(Tlv("60", Truncate(merchantCity, 15)));
        sb.Append(Tlv("62", additionalData));
        sb.Append("6304");

        var crc = Crc16Ccitt(sb.ToString());
        sb.Append(crc.ToString("X4", CultureInfo.InvariantCulture));
        return sb.ToString();
    }

    private static string Tlv(string id, string value) => $"{id}{value.Length:D2}{value}";

    private static string Truncate(string value, int maxLength) => value.Length <= maxLength ? value : value[..maxLength];

    /// <summary>CRC-16/CCITT-FALSE (poly 0x1021, init 0xFFFF, no reflect, xorout 0) — the checksum PIX's BR Code specifies.</summary>
    internal static ushort Crc16Ccitt(string payload)
    {
        const ushort polynomial = 0x1021;
        var crc = (ushort)0xFFFF;

        foreach (var b in Encoding.ASCII.GetBytes(payload))
        {
            crc ^= (ushort)(b << 8);
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 0x8000) != 0 ? (ushort)((crc << 1) ^ polynomial) : (ushort)(crc << 1);
            }
        }

        return crc;
    }
}
