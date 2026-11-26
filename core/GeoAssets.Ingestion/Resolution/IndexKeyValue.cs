using System.Globalization;

namespace GeoAssets.Ingestion.Resolution;

/// <summary>Formats a raw attribute value as an index key (invariant; absent, zero and blank values are not keys).</summary>
internal static class IndexKeyValue
{
    public static string? From(object? value) => value switch
    {
        null => null,
        string text => FromText(text),
        long number => number == 0 ? null : number.ToString(CultureInfo.InvariantCulture),
        int number => number == 0 ? null : number.ToString(CultureInfo.InvariantCulture),
        double number => number == 0 || double.IsNaN(number) ? null : number.ToString("R", CultureInfo.InvariantCulture),
        decimal number => number == 0 ? null : number.ToString(CultureInfo.InvariantCulture),
        IFormattable formattable => FromText(formattable.ToString(null, CultureInfo.InvariantCulture)),
        _ => FromText(value.ToString()),
    };

    private static string? FromText(string? text)
    {
        var trimmed = text?.Trim();
        return string.IsNullOrEmpty(trimmed) || string.Equals(trimmed, "0", StringComparison.Ordinal) ? null : trimmed;
    }
}
