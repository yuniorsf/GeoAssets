using System.Globalization;

namespace GeoAssets.Tools.NeutralOperatorDataMove.Tests.Support;

/// <summary>Reads the numbers the tool's report prints per table.</summary>
internal static class ReportRows
{
    public const int Source = 0;
    public const int Identical = 1;
    public const int Missing = 2;
    public const int Differ = 3;
    public const int Copied = 4;
    public const int Removed = 5;

    /// <summary>The six numbers (Source, Identical, Missing, Differ, Copied, Removed) of <paramref name="table"/>'s line.</summary>
    public static long[] Of(string report, string table)
    {
        var line = report.Split('\n').Single(l => l.StartsWith(table + " ", StringComparison.Ordinal));
        return [.. line[table.Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(v => long.Parse(v, CultureInfo.InvariantCulture))];
    }

    public static long Total(string report, int column) =>
        DatabaseState.Tables
            .Select(t => t.Replace("\"", string.Empty, StringComparison.Ordinal))
            .Sum(t => Of(report, t)[column]);
}
