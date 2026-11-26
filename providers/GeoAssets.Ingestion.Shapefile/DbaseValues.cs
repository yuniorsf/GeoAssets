using System.Globalization;
using NetTopologySuite.IO;

namespace GeoAssets.Ingestion.Shapefile;

/// <summary>
/// Normalizes the values <see cref="DbaseFileReader"/> returns to the contract's types: integers (numeric fields
/// without decimals) as <see cref="long"/>, reals as <see cref="double"/>, dates as <see cref="DateTime"/>, strings
/// trimmed (blank → <c>null</c>).
/// </summary>
internal static class DbaseValues
{
    public static object? Normalize(object? value, DbaseFieldDescriptor field)
    {
        switch (value)
        {
            case null or DBNull:
                return null;
            case string text:
                var trimmed = text.Trim();
                return trimmed.Length == 0 ? null : trimmed;
            case DateTime date:
                return date;
            case bool flag:
                return flag;
        }

        if (value is not IConvertible convertible)
            return value;

        var number = convertible.ToDouble(CultureInfo.InvariantCulture);
        if (double.IsNaN(number) || double.IsInfinity(number))
            return null;
        var integral = field.DecimalCount == 0 && field.DbaseType is 'N' or 'F' or 'I' or '+'
            && Math.Abs(number) < 9.2e18 && Math.Abs(number - Math.Round(number)) < 1e-9;
        return integral ? (object)(long)Math.Round(number) : number;
    }
}
