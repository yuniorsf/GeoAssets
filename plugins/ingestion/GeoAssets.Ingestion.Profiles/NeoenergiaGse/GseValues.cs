using System.Globalization;

namespace GeoAssets.Ingestion.Profiles.NeoenergiaGse;

/// <summary>Reads GSE attribute values: keys (integers without decimals, 0/blank = absent), nodes, text, strings.</summary>
internal static class GseValues
{
    /// <summary>The value as a key: integers formatted invariant without decimals; <c>null</c> when absent, 0 or blank.</summary>
    public static string? Key(IReadOnlyDictionary<string, object?> attributes, string field) =>
        attributes.TryGetValue(field, out var value) ? Key(value) : null;

    public static string? Key(object? value)
    {
        switch (value)
        {
            case null:
                return null;
            case string text:
                var trimmed = text.Trim();
                return trimmed.Length == 0 || IsZero(trimmed) ? null : trimmed;
            case double or float or decimal:
                var real = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                if (!double.IsFinite(real) || real == 0)
                    return null;
                return Math.Abs(real - Math.Round(real)) < 1e-9 && Math.Abs(real) < 9.2e18
                    ? ((long)Math.Round(real)).ToString(CultureInfo.InvariantCulture)
                    : real.ToString("R", CultureInfo.InvariantCulture);
            case IConvertible convertible when value is not bool and not DateTime:
                var integer = convertible.ToInt64(CultureInfo.InvariantCulture);
                return integer == 0 ? null : integer.ToString(CultureInfo.InvariantCulture);
            default:
                return Convert.ToString(value, CultureInfo.InvariantCulture);
        }
    }

    /// <summary>A local node number (the GSE <c>NU_NO*</c> fields), when present and integral.</summary>
    public static int? Node(IReadOnlyDictionary<string, object?> attributes, string field)
    {
        if (!attributes.TryGetValue(field, out var value) || value is null or bool or DateTime)
            return null;
        if (value is string text)
            return int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
        if (value is not IConvertible convertible)
            return null;

        var number = convertible.ToDouble(CultureInfo.InvariantCulture);
        return double.IsFinite(number) && number is >= int.MinValue and <= int.MaxValue ? (int)Math.Round(number) : null;
    }

    /// <summary>The value as trimmed text; <c>null</c> when absent or blank.</summary>
    public static string? Text(IReadOnlyDictionary<string, object?> attributes, string field)
    {
        if (!attributes.TryGetValue(field, out var value) || value is null)
            return null;
        var text = Stringify(value).Trim();
        return text.Length == 0 ? null : text;
    }

    /// <summary>Every non-null raw attribute as an invariant string (long "D", double "R", dates "yyyy-MM-dd").</summary>
    public static Dictionary<string, string> Attributes(IReadOnlyDictionary<string, object?> attributes)
    {
        var result = new Dictionary<string, string>(attributes.Count, StringComparer.Ordinal);
        foreach (var (name, value) in attributes)
        {
            if (value is not null)
                result[name] = Stringify(value);
        }

        return result;
    }

    private static string Stringify(object value) => value switch
    {
        string text => text,
        long integer => integer.ToString("D", CultureInfo.InvariantCulture),
        int integer => integer.ToString("D", CultureInfo.InvariantCulture),
        double real => real.ToString("R", CultureInfo.InvariantCulture),
        float real => real.ToString("R", CultureInfo.InvariantCulture),
        DateTime date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
    };

    private static bool IsZero(string text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && number == 0;
}
